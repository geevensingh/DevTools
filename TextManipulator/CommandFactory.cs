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

        internal static void HideFind_Execute()
        {
            Finder finder = Finder.Get();
            Debug.Assert(finder != null && finder.CanHideWindow);
            finder.HideWindow();
        }

        internal static void HideFind_CanExecute(ref CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Finder.Get().CanHideWindow;
        }
    }
}
