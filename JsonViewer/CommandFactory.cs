using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;

namespace JsonViewer
{
    internal static class CommandFactory
    {
        public static readonly RoutedUICommand NewWindow = new RoutedUICommand(
            text: "New Window",
            name: "NewWindow",
            ownerType: typeof(CommandFactory),
            inputGestures: new InputGestureCollection()
            {
                new KeyGesture(Key.N, ModifierKeys.Control)
            });

        public static readonly RoutedUICommand Reload = new RoutedUICommand(
            text: "Reload",
            name: "Reload",
            ownerType: typeof(CommandFactory),
            inputGestures: new InputGestureCollection()
            {
                new KeyGesture(Key.F5)
            });

        public static readonly RoutedUICommand PickConfig = new RoutedUICommand(
            text: "PickConfig",
            name: "PickConfig",
            ownerType: typeof(CommandFactory),
            inputGestures: new InputGestureCollection()
            {
                new KeyGesture(Key.L, ModifierKeys.Control)
            });

        public static readonly RoutedUICommand HideFind = new RoutedUICommand(
            text: "HideFindWindow",
            name: "HideFindWindow",
            ownerType: typeof(CommandFactory),
            inputGestures: new InputGestureCollection()
            {
                new KeyGesture(Key.Escape)
            });

        internal static void HideFind_Execute(Finder finder)
        {
            finder.HideWindow();
        }
    }
}
