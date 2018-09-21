﻿using Castle.DynamicProxy;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace ChangeTracking
{
    internal class NotifyPropertyChangedInterceptor<T> : IInterceptor, IInterceptorSettings where T : class
    {
        private static Dictionary<string, PropertyInfo> _Properties;
        private readonly Dictionary<string, PropertyChangedEventHandler> _PropertyChangedEventHandlers;
        private readonly Dictionary<string, ListChangedEventHandler> _ListChangedEventHandlers;

        public bool IsInitialized { get; set; }

        static NotifyPropertyChangedInterceptor()
        {
            _Properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(pi => pi.Name);
        }

        internal NotifyPropertyChangedInterceptor(ChangeTrackingInterceptor<T> changeTrackingInterceptor)
        {
            _PropertyChangedEventHandlers = new Dictionary<string, PropertyChangedEventHandler>();
            _ListChangedEventHandlers = new Dictionary<string, ListChangedEventHandler>();
            changeTrackingInterceptor._StatusChanged += (o, e) =>
            {
                RaisePropertyChanged(o, nameof(IChangeTrackable.ChangeTrackingStatus));
                RaisePropertyChanged(o, nameof(IChangeTrackable.ChangedProperties));
            };
            PropertyChanged += delegate { };
        }

        public void Intercept(IInvocation invocation)
        {
            if (!IsInitialized)
            {
                return;
            }
            if (invocation.Method.IsSetter())
            {
                string propertyName = invocation.Method.PropertyName();
                var previousValue = _Properties[propertyName].GetValue(invocation.Proxy, null);
                invocation.Proceed();
                var newValue = _Properties[propertyName].GetValue(invocation.Proxy, null);
                if (!Equals(previousValue, newValue))
                {
                    RaisePropertyChanged(invocation.Proxy, propertyName);
                    RaisePropertyChanged(invocation.Proxy, nameof(IChangeTrackable.ChangedProperties));
                }
                if (!ReferenceEquals(previousValue, newValue))
                {
                    UnsubscribeFromChildPropertyChanged(propertyName, previousValue);
                    SubscribeToChildPropertyChanged(invocation, propertyName, newValue);
                    UnsubscribeFromChildListChanged(propertyName, previousValue);
                    SubscribeToChildListChanged(invocation, propertyName, newValue);
                }
                return;
            }
            if (invocation.Method.IsGetter())
            {
                invocation.Proceed();
                string propertyName = invocation.Method.PropertyName();
                SubscribeToChildPropertyChanged(invocation, propertyName, invocation.ReturnValue);
                SubscribeToChildListChanged(invocation, propertyName, invocation.ReturnValue);
            }
            switch (invocation.Method.Name)
            {
                case "add_PropertyChanged":
                    PropertyChanged += (PropertyChangedEventHandler)invocation.Arguments[0];
                    break;
                case "remove_PropertyChanged":
                    PropertyChanged -= (PropertyChangedEventHandler)invocation.Arguments[0];
                    break;
                default:
                    invocation.Proceed();
                    break;
            }
        }

        private void UnsubscribeFromChildPropertyChanged(string propertyName, object oldChild)
        {
            global::System.ComponentModel.PropertyChangedEventHandler handler;
            var trackable = oldChild as INotifyPropertyChanged;
            if (trackable != null && _PropertyChangedEventHandlers.TryGetValue(propertyName, out handler))
            {
                trackable.PropertyChanged -= handler;
                _PropertyChangedEventHandlers.Remove(propertyName);
            }
        }

        private void SubscribeToChildPropertyChanged(IInvocation invocation, string propertyName, object newValue)
        {
            var newChild = newValue as INotifyPropertyChanged;
            if (newChild!=null && !_PropertyChangedEventHandlers.ContainsKey(propertyName))
            {
                PropertyChangedEventHandler newHandler = (object sender, PropertyChangedEventArgs e) => RaisePropertyChanged(invocation.Proxy, propertyName);
                newChild.PropertyChanged += newHandler;
                _PropertyChangedEventHandlers.Add(propertyName, newHandler);
            }
        }

        private void UnsubscribeFromChildListChanged(string propertyName, object oldChild)
        {
            var trackable = oldChild as IBindingList;
            global::System.ComponentModel.ListChangedEventHandler handler;
            if (trackable !=null && _ListChangedEventHandlers.TryGetValue(propertyName, out handler))
            {
                trackable.ListChanged -= handler;
                _ListChangedEventHandlers.Remove(propertyName);
            }
        }

        private void SubscribeToChildListChanged(IInvocation invocation, string propertyName, object newValue)
        {
            var newChild = newValue as IBindingList;
            if (newChild!=null && !_ListChangedEventHandlers.ContainsKey(propertyName))
            {
                ListChangedEventHandler newHandler = (object sender, ListChangedEventArgs e) => RaisePropertyChanged(invocation.Proxy, propertyName);
                newChild.ListChanged += newHandler;
                _ListChangedEventHandlers.Add(propertyName, newHandler);
            }
        }

        private event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(object proxy, string propertyName)
        {
            PropertyChanged(proxy, new PropertyChangedEventArgs(propertyName));
        }
    }
}
