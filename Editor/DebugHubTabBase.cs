using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Linq;

/// <summary>
/// Base class providing common utilities for debug hub tabs.
/// Tabs can inherit from this for shared functionality.
/// </summary>
public abstract class DebugHubTabBase : IDebugHubTab
{
    public abstract string TabName { get; }

    public abstract void OnGUI();

    public virtual bool ShouldShow() => true;

    public virtual void OnTabSelected() { }

    public virtual void OnTabDeselected() { }

    public virtual bool RequiresUpdate() => false;

    public virtual void OnUpdate() { }

    // Common helper methods that tabs can use

    /// <summary>
    /// Focus scene view on a MonoBehaviour entity
    /// </summary>
    protected void FocusSceneViewOnEntity(MonoBehaviour entity)
    {
        if (entity == null)
        {
            Debug.LogWarning("[DebugHub] Cannot focus scene view on null entity");
            return;
        }

        Vector3 targetPosition = entity.transform.position;

        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            sceneView = EditorWindow.GetWindow<SceneView>();
        }

        Vector3 cameraPosition = FindOptimalSceneViewPosition(targetPosition, entity);

        sceneView.pivot = targetPosition;
        sceneView.rotation = Quaternion.LookRotation(targetPosition - cameraPosition);
        sceneView.size = 10f;

        Selection.activeGameObject = entity.gameObject;
        EditorGUIUtility.PingObject(entity.gameObject);

        sceneView.Focus();
        SceneView.RepaintAll();

        Debug.Log($"[DebugHub] Focused Scene View on {entity.name} at {targetPosition}");
    }

    /// <summary>
    /// Focus scene view on a specific world position
    /// </summary>
    protected void FocusSceneViewOnPosition(Vector3 worldPosition)
    {
        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.Frame(new Bounds(worldPosition, Vector3.one * 5f), false);
            sceneView.Repaint();
            Debug.Log($"[DebugHub] Focused scene view on position: {worldPosition}");
        }
    }

    /// <summary>
    /// Find optimal camera position for scene view
    /// </summary>
    protected Vector3 FindOptimalSceneViewPosition(Vector3 targetPosition, MonoBehaviour entity)
    {
        var camera = Camera.main;
        if (camera == null) return targetPosition + Vector3.back * 10f + Vector3.up * 5f;

        Vector3[] testPositions = {
            targetPosition + Vector3.back * 8f + Vector3.up * 4f,
            targetPosition + Vector3.back * 10f + Vector3.up * 6f,
            targetPosition + Vector3.back * 12f + Vector3.up * 8f,
            targetPosition + Vector3.back * 6f + Vector3.up * 3f,
            targetPosition + Vector3.back * 15f + Vector3.up * 10f
        };

        foreach (var testPos in testPositions)
        {
            Vector3 direction = (targetPosition - testPos).normalized;
            float distance = Vector3.Distance(testPos, targetPosition);

            RaycastHit hit;
            if (Physics.Raycast(testPos, direction, out hit, distance))
            {
                if (hit.collider.gameObject == entity.gameObject ||
                    Vector3.Distance(hit.point, targetPosition) < 1f)
                {
                    return testPos;
                }
            }
            else
            {
                return testPos;
            }
        }

        return targetPosition + Vector3.back * 10f + Vector3.up * 5f;
    }

    /// <summary>
    /// Generic method to draw an entity list with search and focus buttons
    /// </summary>
    protected void DrawEntityList<T>(
        string title,
        T[] entities,
        ref bool showList,
        ref string searchFilter,
        ref Vector2 scrollPosition,
        System.Func<T, string> getName = null,
        System.Action<T> onFocus = null,
        System.Action<T> onSelect = null) where T : MonoBehaviour
    {
        if (getName == null) getName = e => e != null ? e.name : "null";
        if (onFocus == null) onFocus = e => FocusSceneViewOnEntity(e);
        if (onSelect == null) onSelect = e => Selection.activeGameObject = e.gameObject;

        if (entities == null || entities.Length == 0)
        {
            showList = EditorGUILayout.Foldout(showList, $"{title} (0)", true);
            if (showList)
            {
                EditorGUILayout.HelpBox($"No {title.ToLower()} found in the scene.", MessageType.Info);
            }
            return;
        }

        showList = EditorGUILayout.Foldout(showList, $"{title} ({entities.Length})", true);
        if (!showList) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Search filter
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
        searchFilter = EditorGUILayout.TextField(searchFilter);
        if (GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            searchFilter = "";
        }
        EditorGUILayout.EndHorizontal();

        // Filter entities - create local copy of searchFilter for use in lambda
        // Must capture the value before the lambda to avoid ref parameter issues
        string filterValue = searchFilter;
        var filteredEntities = entities.Where(e => e != null &&
            (string.IsNullOrEmpty(filterValue) ||
             getName(e).ToLower().Contains(filterValue.ToLower()))).ToArray();

        if (filteredEntities.Length == 0)
        {
            EditorGUILayout.HelpBox($"No {title.ToLower()} match the search filter.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        // Draw scrollable list
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition,
            GUILayout.Height(Mathf.Min(filteredEntities.Length * 25 + 10, 200)));

        foreach (var entity in filteredEntities)
        {
            if (entity == null) continue;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(getName(entity), GUILayout.Width(200));

            if (GUILayout.Button("Focus", GUILayout.Width(60)))
            {
                onFocus(entity);
            }
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                onSelect(entity);
            }
            if (GUILayout.Button("Properties", GUILayout.Width(80)))
            {
                Selection.activeGameObject = entity.gameObject;
                EditorGUIUtility.PingObject(entity.gameObject);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }
}
