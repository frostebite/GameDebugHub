using System;

namespace GameDebugHub
{
    /// <summary>
    /// Attribute to mark classes as debug hub tabs.
    /// Similar to ToolbarSectionAttribute pattern.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DebugHubTabAttribute : Attribute
    {
        public string TabName { get; }

        /// <summary>
        /// Optional order for sorting tabs. Lower values appear first.
        /// </summary>
        public int Order { get; set; } = 1000;

        public DebugHubTabAttribute(string tabName)
        {
            TabName = tabName;
        }
    }
}
