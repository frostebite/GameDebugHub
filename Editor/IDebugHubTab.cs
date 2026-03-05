using UnityEditor;

namespace GameDebugHub
{
    /// <summary>
    /// Interface for debug hub tabs. Similar to IEditorToolbar pattern.
    /// </summary>
    public interface IDebugHubTab
    {
        /// <summary>
        /// Display name of the tab shown in the toolbar
        /// </summary>
        string TabName { get; }

        /// <summary>
        /// Called every frame to render the tab content
        /// </summary>
        void OnGUI();

        /// <summary>
        /// Determines if this tab should be shown in the debug hub.
        /// Useful for conditionally showing tabs based on runtime state or preferences.
        /// </summary>
        bool ShouldShow() => true;

        /// <summary>
        /// Called when the tab becomes selected/active.
        /// Use this for initialization that only needs to happen when tab is visible.
        /// </summary>
        void OnTabSelected() { }

        /// <summary>
        /// Called when the tab is deselected/switched away.
        /// Use this for cleanup or saving state.
        /// </summary>
        void OnTabDeselected() { }

        /// <summary>
        /// Called every editor update. Return true if tab needs repaint.
        /// Generic hub will call Repaint() on the window if this returns true.
        /// </summary>
        bool RequiresUpdate() => false;

        /// <summary>
        /// Optional: Called during editor update loop for tabs that need continuous updates.
        /// Only called if RequiresUpdate() returns true.
        /// </summary>
        void OnUpdate() { }
    }
}
