using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace JsonViewer
{
    class NotifyPropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetValue<T>(ref T member, T newValue, string propertyName)
        {
            if (!member.Equals(newValue))
            {
                member = newValue;
                this.FirePropertyChanged(propertyName);
                return true;
            }
            return false;
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