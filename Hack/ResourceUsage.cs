using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Xml;

namespace Hack
{
    class ResourceUsage
    {
        public string File = string.Empty;
        public string NodeName = string.Empty;
        public string ResourceName = string.Empty;
        public string ResourceType = string.Empty;

        public static ResourceUsage[] Create(XmlReader reader, string filePath)
        {
            string nodeName = reader.Name;
            Debug.Assert(!string.IsNullOrEmpty(nodeName));
            if (nodeName == "Setter")
            {
                string valueName = reader.GetAttribute("Value");
                if (string.IsNullOrEmpty(valueName))
                {
                    return null;
                }

                ResourceUsage usage = ParseValue(valueName);
                if (usage == null)
                {
                    return null;
                }
                usage.File = filePath;
                string nodeSubname = reader.GetAttribute("Target");
                if (string.IsNullOrEmpty(nodeSubname))
                {
                    nodeSubname = reader.GetAttribute("Property");
                }
                Debug.Assert(!string.IsNullOrEmpty(nodeSubname));
                usage.NodeName = nodeName + " : " + nodeSubname;
                return new ResourceUsage[] { usage };
            }

            for (int ii = 0; ii < reader.AttributeCount; ii++)
            {
                reader.GetAttribute(ii);
            }

            if (reader.HasAttributes)
            {
                List<ResourceUsage> usages = new List<ResourceUsage>();
                while (reader.MoveToNextAttribute())
                {
                    ResourceUsage usage = ParseValue(reader.Value);
                    if (usage != null)
                    {
                        usage.File = filePath;
                        usage.NodeName = nodeName + " : " + reader.Name;
                        usages.Add(usage);
                    }
                };
                return usages.ToArray();
            }

            return null;
        }

        private static ResourceUsage ParseValue(string value)
        {
            if (!value.StartsWith("{") || !value.EndsWith("}"))
            {
                return null;
            }

            string trimmed = value.Substring(1, value.Length - 2).Trim();
            if (trimmed == "x:Null")
            {
                return null;
            }

            if (trimmed.Contains("{"))
            {
                Debug.Assert(trimmed.Contains("}"));
                //Debug.WriteLine("Unhandled value: " + value);
                return null;
            }

            string[] parts = trimmed.Split(new char[] { ' ' });
            if (parts[0] == "Binding" || parts[0] == "x:Bind")
            {
                //Debug.WriteLine("Unhandled value: " + value);
                return null;
            }

            Debug.Assert(parts.Length == 2);
            Debug.Assert(parts[0] == "StaticResource" || parts[0] == "ThemeResource" || parts[0] == "CustomResource" || parts[0] == "TemplateBinding");

            if (parts[0] == "TemplateBinding")
            {
#if false
                if (!allowedTemplateBindings.Contains(parts[1]))
                {
                    Debug.WriteLine("Unknown TemplateBinding: " + parts[1]);
                }
#else
                Debug.Assert(allowedTemplateBindings.Contains(parts[1]));
#endif
                return null;
            }

            ResourceUsage usage = new ResourceUsage();
            usage.ResourceType = parts[0];
            usage.ResourceName = parts[1];

            return usage;
        }

        private static string[] allowedTemplateBindings = new string[] {
            "AccessibilityString",
            "AccessView",
            "Background",
            "BackgroundBrush",
            "BorderBrush",
            "BorderThickness",
            "Brush",
            "CancelButtonAccessibleName",
            "ComputedHorizontalScrollBarVisibility",
            "ComputedVerticalScrollBarVisibility",
            "Content",
            "ContentTemplate",
            "ContentTransitions",
            "DismissButtonCollapseAccessibleName",
            "DisplayMemberPath",
            "FailureToolTipMessage",
            "FlowDirection",
            "FocusVisualMargin",
            "FocusVisualPrimaryBrush",
            "FocusVisualPrimaryThickness",
            "FocusVisualSecondaryBrush",
            "FocusVisualSecondaryThickness",
            "FontFamily",
            "FontSize",
            "FontWeight",
            "Footer",
            "FooterTemplate",
            "FooterTransitions",
            "Foreground",
            "Header",
            "HeaderTemplate",
            "HeaderTransitions",
            "Height",
            "HorizontalAlignment",
            "HorizontalContentAlignment",
            "HorizontalOffset",
            "Icon",
            "IconVisibility",
            "ImageControl",
            "InnerIconStyle",
            "InnerTextStyle",
            "ItemContainerStyle",
            "ItemsSource",
            "ItemTemplate",
            "ItemTemplateSelector",
            "Label",
            "Languages",
            "ListAutomationId",
            "ListAutomationName",
            "ListFooter",
            "ListFooterTemplate",
            "ListHeader",
            "ListHeaderBackground",
            "ListHeaderTemplate",
            "ListItemContainerStyle",
            "ListItemsPanelTemplate",
            "ListItemTemplate",
            "ListItemTemplateSelector",
            "ManageDevicesUri",
            "Margin",
            "MaxHeight",
            "MaxWidth",
            "MinHeight",
            "MinWidth",
            "MvrIcon",
            "NextButtonAccessibleName",
            "OffContent",
            "OffContentTemplate",
            "OnContent",
            "OnContentTemplate",
            "OverflowItemTemplate",
            "Padding",
            "Pane",
            "ParallaxScrollViewerContainer",
            "ParallaxTargetElement",
            "PlaceHolderIconVisibility",
            "PlaceholderText",
            "PosterSource",
            "PreviousButtonAccessibleName",
            "Resolutions",
            "ScrollableHeight",
            "ScrollableWidth",
            "ScrollViewer.BringIntoViewOnFocusChange",
            "ScrollViewer.HorizontalScrollBarVisibility",
            "ScrollViewer.HorizontalScrollMode",
            "ScrollViewer.IsDeferredScrollingEnabled",
            "ScrollViewer.IsHorizontalRailEnabled",
            "ScrollViewer.IsHorizontalScrollChainingEnabled",
            "ScrollViewer.IsVerticalRailEnabled",
            "ScrollViewer.IsVerticalScrollChainingEnabled",
            "ScrollViewer.VerticalScrollBarVisibility",
            "ScrollViewer.VerticalScrollMode",
            "ScrollViewer.ZoomMode",
            "SecondaryButtonText",
            "SecondaryListHeader",
            "SecondaryTextButtonIsTabStop",
            "SeparatorLineVisibility",
            "StickyHeaderContent",
            "Stretch",
            "Stroke",
            "TabNavigation",
            "Text",
            "TextBoxStyle",
            "TextWrapping",
            "Title",
            "TitleTemplate",
            "ToolTip",
            "utils:Focus.AdditionalFocusPaddingForContentPeek",
            "Value",
            "VerticalAlignment",
            "VerticalContentAlignment",
            "VerticalOffset",
            "ViewportHeight",
            "ViewportWidth",
            "Width",
            "ZoomedInView",
            "ZoomedOutView"
        };
    }
}
