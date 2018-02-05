using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

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
    }
}
