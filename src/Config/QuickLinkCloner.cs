using System.Collections.Generic;
using System.ComponentModel;

namespace InspectionWidget.Config
{
    /// <summary>快捷链接深拷贝：配置、草稿、导入计划之间不共享条目引用。</summary>
    public static class QuickLinkCloner
    {
        public static QuickLinkEntry Clone(QuickLinkEntry source)
        {
            if (source == null)
                return null;
            return new QuickLinkEntry
            {
                Title = source.Title,
                Url = source.Url,
                Enabled = source.Enabled
            };
        }

        public static List<QuickLinkEntry> CloneList(IEnumerable<QuickLinkEntry> source)
        {
            var result = new List<QuickLinkEntry>();
            if (source == null)
                return result;
            foreach (QuickLinkEntry entry in source)
                result.Add(Clone(entry));
            return result;
        }

        public static BindingList<QuickLinkEntry> CloneToBindingList(IEnumerable<QuickLinkEntry> source)
        {
            var result = new BindingList<QuickLinkEntry>();
            ReplaceInto(result, source);
            return result;
        }

        public static void ReplaceInto(BindingList<QuickLinkEntry> target,
            IEnumerable<QuickLinkEntry> source)
        {
            if (target == null)
                return;
            target.Clear();
            if (source == null)
                return;
            foreach (QuickLinkEntry entry in source)
                target.Add(Clone(entry));
        }
    }
}
