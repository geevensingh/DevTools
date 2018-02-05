using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace TextManipulator
{
    class NotifyPropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void SetValue<T>(ref T member, T newValue, string propertyName)
        {
            if (!member.Equals(newValue))
            {
                member = newValue;
                this.FirePropertyChanged(propertyName);
            }
        }
        protected void FirePropertyChanged(string propertyName)
        {
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}