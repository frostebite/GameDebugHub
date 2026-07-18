using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameDebugHub
{
    /// <summary>
    /// Generic Game Debug Hub that discovers and displays tabs via reflection across all assemblies.
    /// Similar to GenericToolbar pattern - tabs can be defined in any assembly/submodule.
    /// Core infrastructure is in SharedEditorCode, tabs can be anywhere.
    /// </summary>
    public class GenericGameDebugHub : EditorWindow
    {
        /// <summary>
        /// Fired whenever a dev hub tab is selected -- either interactively via the tab bar
        /// ("button") or programmatically via <see cref="SelectTabById"/> ("external", e.g. a
        /// cross-tool dispatch from Buddy Desktop's dashboard open-debug-tab endpoint). Discrete
        /// selection event only, never raised from OnGUI/OnEditorUpdate's per-frame paths.
        /// Parameters: tab name, framework label, module label, trigger. GameDebugHub has no
        /// dependency on SharedEditorCode/analytics, so consumers (e.g. editor usage telemetry)
        /// subscribe from an assembly that references this one, not the other way around.
        /// </summary>
        public static event Action<string, string, string, string> TabSelected;

        private static readonly List<TabInfo> _tabs = new List<TabInfo>();
        private static bool _tabsLoaded = false;

        private int _selectedTab = 0;
        private Vector2 _scrollPosition;
        private int _previousSelectedTab = -1;
        private bool _showDebugInfo = false;

        private class TabInfo
        {
            public string Name;
            public IDebugHubTab Instance;
            public int Order;
            public string Framework;
            public string Module;
            public string AssemblyName;
            public string TypeName;
            public string DisplayName
            {
                get
                {
                    if (string.IsNullOrEmpty(Module))
                        return $"{Framework} - {Name}";
                    return $"{Framework} {Module} - {Name}";
                }
            }
        }

        static GenericGameDebugHub()
        {
            LoadTabs();
        }

        private static void LoadTabs()
        {
            if (_tabsLoaded) return;

            _tabs.Clear();

            int loadedCount = 0;
            int skippedCount = 0;

            // Use TypeCache to find all classes with DebugHubTabAttribute across all assemblies
            // This automatically discovers tabs from any assembly/submodule
            foreach (var type in TypeCache.GetTypesWithAttribute<DebugHubTabAttribute>())
            {
                if (typeof(IDebugHubTab).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    try
                    {
                        var attr = type.GetCustomAttribute<DebugHubTabAttribute>();
                        if (attr == null)
                        {
                            Debug.LogWarning($"[GenericGameDebugHub] DebugHubTabAttribute not found on type {type.Name}, skipping");
                            skippedCount++;
                            continue;
                        }

                        var instance = Activator.CreateInstance(type) as IDebugHubTab;
                        if (instance == null)
                        {
                            Debug.LogWarning($"[GenericGameDebugHub] Failed to create instance of {type.Name}, skipping");
                            skippedCount++;
                            continue;
                        }

                        var (framework, module) = GetFrameworkAndModuleFromType(type);
                        var assemblyName = type.Assembly.GetName().Name;

                        _tabs.Add(new TabInfo
                        {
                            Name = attr.TabName,
                            Instance = instance,
                            Order = attr.Order,
                            Framework = framework,
                            Module = module,
                            AssemblyName = assemblyName,
                            TypeName = type.FullName
                        });

                        loadedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[GenericGameDebugHub] Failed to create tab {type.Name} from assembly {type.Assembly.GetName().Name}: {ex.Message}\n{ex.StackTrace}");
                        skippedCount++;
                    }
                }
            }

            // Sort tabs by order, then by framework, module, and name (similar to toolbar)
            _tabs.Sort((a, b) =>
            {
                if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
                if (a.Framework != b.Framework) return string.Compare(a.Framework, b.Framework, StringComparison.Ordinal);
                if (a.Module != b.Module) return string.Compare(a.Module, b.Module, StringComparison.Ordinal);
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            _tabsLoaded = true;

            Debug.Log($"[GenericGameDebugHub] Loaded {loadedCount} debug hub tabs from {_tabs.Select(t => t.AssemblyName).Distinct().Count()} assembly(ies). Skipped {skippedCount}.");
        }

        /// <summary>
        /// Extract framework and module information from type's namespace/assembly path.
        /// Uses a generic approach: extracts the folder name after "Submodules/" in the asset path,
        /// or falls back to namespace segments or the assembly name. No hardcoded project names.
        /// </summary>
        private static (string Framework, string Module) GetFrameworkAndModuleFromType(Type type)
        {
            // Try to find the script file using AssetDatabase and extract from path
            try
            {
                var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        var result = ExtractFrameworkAndModuleFromPath(assetPath);
                        if (result.Framework != "General")
                            return result;
                    }
                }
            }
            catch
            {
                // Fall through to namespace-based detection
            }

            // Fallback: derive a label from the namespace
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                var segments = type.Namespace.Split('.');
                // Use the first meaningful namespace segment as the framework label
                if (segments.Length >= 1)
                {
                    var label = segments[0];
                    var module = segments.Length >= 2 ? segments[1] : "";
                    return (label, module);
                }
            }

            // Last resort: use the assembly name
            var assemblyName = type.Assembly.GetName().Name;
            return (assemblyName, "");
        }

        /// <summary>
        /// Extract framework and module from asset path.
        /// Looks for a "Submodules/" segment and uses the folder name immediately after it as the
        /// framework label. If no Submodules/ segment is found, looks for "Shared" or falls back
        /// to "General". This approach is fully generic -- no project-specific strings.
        /// </summary>
        private static (string Framework, string Module) ExtractFrameworkAndModuleFromPath(string path)
        {
            path = path.Replace("\\", "/");

            // Look for the Submodules/ segment and extract the folder name after it
            var segments = path.Split('/');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (string.Equals(segments[i], "Submodules", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < segments.Length)
                {
                    var submoduleName = segments[i + 1];
                    // If there is another meaningful folder after the submodule name, use it as the module
                    var module = (i + 2 < segments.Length && !segments[i + 2].Contains("."))
                        ? segments[i + 2]
                        : "";
                    return (submoduleName, module);
                }
            }

            // Check for Shared folder pattern
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], "Shared", StringComparison.OrdinalIgnoreCase))
                    return ("Shared", "");
            }

            return ("General", "");
        }

        [MenuItem("Tools/Game Debug Hub")]
        public static void ShowWindow()
        {
            var window = GetWindow<GenericGameDebugHub>("Game Debug Hub");
            window.minSize = new Vector2(600, 400);
        }

        /// <summary>
        /// Stable slug for a tab, matching BuddyLaunchManifestWriter.MakeStableId (derived from the
        /// tab's declaring type full name). Used to resolve an external "open this tab" request
        /// (e.g. from Battle Buddy Desktop's dashboard HTTP endpoint) into a concrete tab index.
        /// </summary>
        private static string MakeStableId(Type type)
        {
            var raw = type.FullName ?? type.Name;
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Opens (or focuses) the Game Debug Hub window and selects the tab whose stable id matches.
        /// Returns true if a matching tab was found and selected, false otherwise. Safe to call from
        /// any external dispatch point (e.g. EditorDashboardServer's open-debug-tab endpoint) as long
        /// as the call is marshaled onto the Unity main thread first.
        /// </summary>
        public static bool SelectTabById(string tabId)
        {
            if (string.IsNullOrEmpty(tabId)) return false;

            _tabsLoaded = false;
            LoadTabs();

            var visibleTabs = _tabs.Where(t => t.Instance.ShouldShow()).ToList();
            var index = visibleTabs.FindIndex(t => string.Equals(MakeStableId(t.Instance.GetType()), tabId, StringComparison.Ordinal));
            if (index < 0) return false;

            var selectedTabInfo = visibleTabs[index];

            var window = GetWindow<GenericGameDebugHub>("Game Debug Hub");
            window.minSize = new Vector2(600, 400);
            window._selectedTab = index;
            window._previousSelectedTab = index;
            EditorPrefs.SetInt("GenericGameDebugHub_SelectedTab", index);
            TabSelected?.Invoke(selectedTabInfo.Name, selectedTabInfo.Framework, selectedTabInfo.Module, "external");
            window.Focus();
            window.Repaint();
            return true;
        }

        private void OnEnable()
        {
            // Reload tabs in case new ones were added (e.g., after recompilation)
            _tabsLoaded = false;
            LoadTabs();

            // Load selected tab from preferences
            _selectedTab = EditorPrefs.GetInt("GenericGameDebugHub_SelectedTab", 0);
            _previousSelectedTab = _selectedTab;

            // Load debug info preference
            _showDebugInfo = EditorPrefs.GetBool("GenericGameDebugHub_ShowDebugInfo", false);

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            // Notify current tab it's being deselected
            var visibleTabs = _tabs.Where(t => t.Instance.ShouldShow()).ToList();
            if (_selectedTab >= 0 && _selectedTab < visibleTabs.Count)
            {
                visibleTabs[_selectedTab].Instance.OnTabDeselected();
            }

            // Save preferences
            EditorPrefs.SetBool("GenericGameDebugHub_ShowDebugInfo", _showDebugInfo);
        }

        private void OnEditorUpdate()
        {
            // Update tabs that require updates
            var visibleTabs = _tabs.Where(t => t.Instance.ShouldShow()).ToList();

            foreach (var tab in visibleTabs)
            {
                if (tab.Instance.RequiresUpdate())
                {
                    tab.Instance.OnUpdate();

                    // Request repaint if this is the active tab
                    var tabIndex = visibleTabs.IndexOf(tab);
                    if (tabIndex == _selectedTab)
                    {
                        Repaint();
                    }
                }
            }
        }

        private void OnGUI()
        {
            // Header with reload button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Game Debug Hub", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload Tabs", GUILayout.Width(100)))
            {
                _tabsLoaded = false;
                LoadTabs();
                Repaint();
            }
            _showDebugInfo = GUILayout.Toggle(_showDebugInfo, "Debug Info", GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            // Show debug info if enabled
            if (_showDebugInfo)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Total Tabs: {_tabs.Count}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Assemblies: {_tabs.Select(t => t.AssemblyName).Distinct().Count()}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Frameworks: {string.Join(", ", _tabs.Select(t => t.Framework).Distinct())}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();

            // Filter tabs that should be shown
            var visibleTabs = _tabs.Where(t => t.Instance.ShouldShow()).ToList();

            if (visibleTabs.Count == 0)
            {
                EditorGUILayout.HelpBox("No debug tabs available. Ensure debug tab classes are marked with [DebugHubTabAttribute] and implement IDebugHubTab.", MessageType.Info);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Debug Info:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Total tabs discovered: {_tabs.Count}", EditorStyles.miniLabel);
                if (_tabs.Count > 0)
                {
                    EditorGUILayout.LabelField("Tabs filtered out by ShouldShow():", EditorStyles.miniLabel);
                    foreach (var tab in _tabs.Where(t => !t.Instance.ShouldShow()))
                    {
                        EditorGUILayout.LabelField($"  - {tab.Name} ({tab.Framework} {tab.Module})", EditorStyles.miniLabel);
                    }
                }
                return;
            }

            // Ensure selected tab is valid
            if (_selectedTab >= visibleTabs.Count)
            {
                _selectedTab = 0;
            }

            // Check if tab selection changed
            if (_previousSelectedTab != _selectedTab)
            {
                // Notify old tab it's being deselected
                if (_previousSelectedTab >= 0 && _previousSelectedTab < visibleTabs.Count)
                {
                    visibleTabs[_previousSelectedTab].Instance.OnTabDeselected();
                }

                // Notify new tab it's being selected
                if (_selectedTab >= 0 && _selectedTab < visibleTabs.Count)
                {
                    var newTab = visibleTabs[_selectedTab];
                    newTab.Instance.OnTabSelected();
                    TabSelected?.Invoke(newTab.Name, newTab.Framework, newTab.Module, "button");
                }

                _previousSelectedTab = _selectedTab;
                EditorPrefs.SetInt("GenericGameDebugHub_SelectedTab", _selectedTab);
            }

            // Tab selection toolbar
            _selectedTab = DrawWrappedToolbar(_selectedTab, visibleTabs.Select(t => t.Name).ToArray());

            // Show current tab info if debug enabled
            if (_showDebugInfo && _selectedTab >= 0 && _selectedTab < visibleTabs.Count)
            {
                var currentTab = visibleTabs[_selectedTab];
                EditorGUILayout.BeginHorizontal(EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Assembly: {currentTab.AssemblyName}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Framework: {currentTab.Framework} {currentTab.Module}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Order: {currentTab.Order}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Draw selected tab
            if (_selectedTab >= 0 && _selectedTab < visibleTabs.Count)
            {
                var currentTab = visibleTabs[_selectedTab];
                try
                {
                    currentTab.Instance.OnGUI();
                }
                catch (Exception ex)
                {
                    EditorGUILayout.HelpBox($"Error in tab '{currentTab.Name}': {ex.Message}", MessageType.Error);
                    if (_showDebugInfo)
                    {
                        EditorGUILayout.LabelField($"Type: {currentTab.TypeName}", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"Assembly: {currentTab.AssemblyName}", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"Stack Trace:", EditorStyles.miniLabel);
                        EditorGUILayout.TextArea(ex.StackTrace, GUILayout.Height(100));
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private int DrawWrappedToolbar(int selectedTab, string[] tabNames)
        {
            float windowWidth = position.width;
            float buttonWidth = 80f;
            float buttonHeight = 24f;
            float spacing = 2f;

            int buttonsPerRow = Mathf.Max(1, Mathf.FloorToInt((windowWidth - 20f) / (buttonWidth + spacing)));

            int currentTab = selectedTab;

            for (int i = 0; i < tabNames.Length; i += buttonsPerRow)
            {
                EditorGUILayout.BeginHorizontal();

                for (int j = 0; j < buttonsPerRow && i + j < tabNames.Length; j++)
                {
                    int tabIndex = i + j;
                    bool isSelected = tabIndex == selectedTab;

                    GUI.backgroundColor = isSelected ? Color.green : Color.white;

                    if (GUILayout.Button(tabNames[tabIndex], GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                    {
                        currentTab = tabIndex;
                    }

                    GUI.backgroundColor = Color.white;

                    if (j < buttonsPerRow - 1 && i + j + 1 < tabNames.Length)
                    {
                        GUILayout.Space(spacing);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            return currentTab;
        }
    }
}
