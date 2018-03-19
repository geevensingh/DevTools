namespace Utilities
{
    using System.Collections.Generic;
    using System.ComponentModel;

    public class NotifyPropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public static bool SetValue<T>(ref T member, T newValue, string propertyName, INotifyPropertyChanged sender, PropertyChangedEventHandler propertyChangedEvent)
        {
            return SetValue(ref member, newValue, new string[] { propertyName }, sender, propertyChangedEvent);
        }

        public static bool SetValue<T>(ref T member, T newValue, string[] propertyNames, INotifyPropertyChanged sender, PropertyChangedEventHandler propertyChangedEvent)
        {
            bool isSameValue = false;

            if (member == null)
            {
                isSameValue = newValue == null;
            }
            else
            {
                isSameValue = member.Equals(newValue);
            }

            if (!isSameValue)
            {
                member = newValue;
                FirePropertyChanged(propertyNames, sender, propertyChangedEvent);
                return true;
            }

            return false;
        }

        public static void FirePropertyChanged(string propertyName, INotifyPropertyChanged sender, PropertyChangedEventHandler propertyChangedEvent)
        {
            FirePropertyChanged(new string[] { propertyName }, sender, propertyChangedEvent);
        }

        public static void FirePropertyChanged(string[] propertyNames, INotifyPropertyChanged sender, PropertyChangedEventHandler propertyChangedEvent)
        {
            foreach (string propertyName in propertyNames)
            {
                propertyChangedEvent?.Invoke(sender, new PropertyChangedEventArgs(propertyName));
            }
        }

        protected bool SetValue<T>(ref T member, T newValue, string propertyName)
        {
            return NotifyPropertyChanged.SetValue(ref member, newValue, propertyName, this, this.PropertyChanged);
        }

        protected bool SetValue<T>(ref T member, T newValue, string[] propertyNames)
        {
            return NotifyPropertyChanged.SetValue(ref member, newValue, propertyNames, this, this.PropertyChanged);
        }

        protected void FirePropertyChanged(string propertyName)
        {
            NotifyPropertyChanged.FirePropertyChanged(new string[] { propertyName }, this, this.PropertyChanged);
        }

        protected void FirePropertyChanged(string[] propertyNames)
        {
            NotifyPropertyChanged.FirePropertyChanged(propertyNames, this, this.PropertyChanged);
        }

        protected bool SetValueList<T>(ref List<T> memberList, List<T> newList, string propertyName)
        {
            if (AreListsSame(memberList, newList))
            {
                return false;
            }

            memberList = newList;
            this.FirePropertyChanged(propertyName);
            return true;
        }

        private bool AreListsSame<T>(List<T> first, List<T> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            for (int ii = 0; ii < first.Count; ii++)
            {
                T firstItem = first[ii];
                T secondItem = second[ii];

                if (firstItem == null && secondItem == null)
                {
                    continue;
                }

                if (firstItem == null && secondItem != null)
                {
                    return false;
                }

                if (secondItem != null && firstItem == null)
                {
                    return false;
                }

                if (!firstItem.Equals(secondItem))
                {
                    return false;
                }
            }

            return true;
        }
    }
}