namespace JsonViewer
{
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
            NotifyPropertyChanged.FirePropertyChanged(propertyName, this, this.PropertyChanged);
        }
    }
}