using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;

namespace TextManipulator
{
    internal static class CommandFactory
    {
        public static readonly RoutedUICommand HideFind = new RoutedUICommand(
            text: "HideFindWindow",
            name: "HideFindWindow",
            ownerType: typeof(CommandFactory),
            inputGestures: new InputGestureCollection()
            {
                new KeyGesture(Key.Escape)
            });

        internal static void HideFind_Execute(FindWindow findWindow)
        {
            Debug.Assert(findWindow != null && findWindow.IsVisible);
            findWindow.Hide();
        }

        internal static void HideFind_CanExecute(FindWindow findWindow, ref CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = findWindow != null && findWindow.IsVisible;
        }
    }
}
