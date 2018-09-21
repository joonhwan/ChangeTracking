﻿using Castle.DynamicProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace ChangeTracking
{
    internal sealed class ChangeTrackingInterceptor<T> : IInterceptor, IInterceptorSettings where T : class
    {
        private Dictionary<string, object> _OriginalValueDictionary;
        internal EventHandler _StatusChanged = delegate { };
        private static Dictionary<string, PropertyInfo> _Properties;
        private ChangeStatus _ChangeTrackingStatus;
        private readonly Dictionary<string, Delegate> _StatusChangedEventHandlers;
        private bool _InRejectChanges;

        public bool IsInitialized { get; set; }

        static ChangeTrackingInterceptor()
        {
            _Properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).ToDictionary(pi => pi.Name);
        }

        internal ChangeTrackingInterceptor(ChangeStatus status)
        {
            _OriginalValueDictionary = new Dictionary<string, object>();
            _StatusChangedEventHandlers = new Dictionary<string, Delegate>();
            _ChangeTrackingStatus = status;
        }

        public void Intercept(IInvocation invocation)
        {
            if (!IsInitialized)
            {
                return;
            }
            if (invocation.Method.IsSetter() && !_InRejectChanges)
            {
                if (_ChangeTrackingStatus == ChangeStatus.Deleted)
                {
                    throw new InvalidOperationException("Can not modify deleted object");
                }
                string propertyName = invocation.Method.PropertyName();
                bool noOriginalValueFound = !_OriginalValueDictionary.ContainsKey(propertyName);

                object originalValue = noOriginalValueFound ? _Properties[propertyName].GetValue(invocation.Proxy, null) : _OriginalValueDictionary[propertyName];
                invocation.Proceed();
                object newValue = _Properties[propertyName].GetValue(invocation.Proxy, null);

                if (!ReferenceEquals(originalValue, newValue))
                {
                    UnsubscribeFromChildStatusChanged(propertyName, originalValue);
                    SubscribeToChildStatusChanged(invocation.Proxy, propertyName, newValue);
                }
                if (noOriginalValueFound && !Equals(originalValue, newValue))
                {
                    _OriginalValueDictionary.Add(propertyName, originalValue);
                    SetAndRaiseStatusChanged(invocation.Proxy, false);
                }
                else if (!noOriginalValueFound && Equals(originalValue, newValue))
                {
                    _OriginalValueDictionary.Remove(propertyName);
                    SetAndRaiseStatusChanged(invocation.Proxy, false);
                }
                return;
            }
            else if (invocation.Method.IsGetter())
            {
                string propertyName = invocation.Method.PropertyName();
                if (propertyName == nameof(IChangeTrackable.ChangeTrackingStatus))
                {
                    invocation.ReturnValue = _ChangeTrackingStatus;
                }
                else if (propertyName == nameof(System.ComponentModel.IChangeTracking.IsChanged))
                {
                    invocation.ReturnValue = _ChangeTrackingStatus != ChangeStatus.Unchanged;
                }
                else if (propertyName == nameof(IChangeTrackable.ChangedProperties))
                {
                    invocation.ReturnValue = GetChangedProperties();
                }
                else
                {
                    invocation.Proceed();
                    SubscribeToChildStatusChanged(invocation.Proxy, propertyName, invocation.ReturnValue);
                }
                return;
            }
            switch (invocation.Method.Name)
            {
                case nameof(IChangeTrackable<object>.GetOriginalValue):
                    invocation.ReturnValue = ((dynamic)this).GetOriginalValue((T)invocation.Proxy, (dynamic)invocation.Arguments[0]);
                    break;
                case nameof(IChangeTrackable<object>.GetOriginal):
                    invocation.ReturnValue = GetOriginal((T)invocation.Proxy);
                    break;
                case nameof(IChangeTrackable<object>.GetCurrent):
                    invocation.ReturnValue = GetCurrent((T)invocation.Proxy);
                    break;
                case "add_StatusChanged":
                    _StatusChanged += (EventHandler)invocation.Arguments[0];
                    break;
                case "remove_StatusChanged":
                    _StatusChanged -= (EventHandler)invocation.Arguments[0];
                    break;
                case "Delete":
                    invocation.ReturnValue = Delete(invocation.Proxy);
                    break;
                case "UnDelete":
                    invocation.ReturnValue = UnDelete(invocation.Proxy);
                    break;
                case "AcceptChanges":
                    AcceptChanges(invocation.Proxy);
                    break;
                case "RejectChanges":
                    RejectChanges(invocation.Proxy);
                    break;
                default:
                    invocation.Proceed();
                    break;
            }
        }

        private object GetOriginalValue(T target, string propertyName)
        {
            object value;
            if (!_OriginalValueDictionary.TryGetValue(propertyName, out value))
            {
                try
                {
                    value = _Properties[propertyName].GetValue(target, null);
                }
                catch (KeyNotFoundException ex)
                {
                    throw new ArgumentOutOfRangeException($"\"{propertyName}\" is not a valid property name of type \"{typeof(T)}\"", ex);
                }
            }
            return value;
        }

        private TResult GetOriginalValue<TResult>(T target, Expression<Func<T, TResult>> selector)
        {
            var propertyInfo = GetPropertyInfo(selector);

            string propName = propertyInfo.Name;
            object value;
            TResult originalValue = _OriginalValueDictionary.TryGetValue(propName, out value) ?
                (TResult)value : 
                selector.Compile()(target);
            originalValue.DoIfType<IChangeTrackableInternal>(trackable =>
            {
                originalValue = (TResult) trackable.GetOriginal();
            });
            return originalValue;
        }

        public PropertyInfo GetPropertyInfo<TSource, TResult>(Expression<Func<TSource, TResult>> propertyLambda)
        {
            MemberExpression member = propertyLambda.Body as MemberExpression;
            if (member == null)
            {
                throw new ArgumentException(string.Format("Expression '{0}' refers to a method, not a property.", propertyLambda));
            }

            PropertyInfo propInfo = member.Member as PropertyInfo;
            if (propInfo == null)
            {
                throw new ArgumentException(string.Format("Expression '{0}' refers to a field, not a property.", propertyLambda));
            }

            Type type = typeof(TSource);
            if (type != propInfo.ReflectedType && !type.IsSubclassOf(propInfo.ReflectedType))
            {
                throw new ArgumentException(string.Format("Expression '{0}' refers to a property that is not from type {1}.", propertyLambda, type));
            }
            return propInfo;
        }

        private T GetOriginal(T proxy)
        {
            var original = Activator.CreateInstance<T>();
            foreach (var property in _Properties.Values)
            {
                object value;
                object originalValue = _OriginalValueDictionary.TryGetValue(property.Name, out value) ? value : property.GetValue(proxy, null);
                if (originalValue != null)
                {
                    if(originalValue.DoIfType<IChangeTrackableInternal>(trackable => { originalValue = trackable.GetOriginal(); }))
                    {
                    }
                    else if (originalValue.GetType().GetInterface("IChangeTrackableCollection`1") != null)
                    {
                        IEnumerable<object> originalValues = ((System.Collections.IEnumerable)originalValue).Cast<IChangeTrackableInternal>().Select(v => v.GetOriginal());
                        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(property.PropertyType.GetGenericArguments().First()));
                        foreach (var item in originalValues)
                        {
                            list.Add(item);
                        }
                        originalValue = list;
                    }
                }
                property.SetValue(original, originalValue, null);
            }
            return original;
        }

        private T GetCurrent(T proxy)
        {
            var current = Activator.CreateInstance<T>();
            foreach (var property in _Properties.Values)
            {
                object currentValue = property.GetValue(proxy, null);
                if (currentValue != null)
                {
                    if (currentValue.DoIfType<IChangeTrackableInternal>(trackable => { currentValue = trackable.GetCurrent(); }))
                    {
                    }
                    else if (currentValue.GetType().GetInterface("IChangeTrackableCollection`1") != null)
                    {
                        IEnumerable<object> originalValues = ((System.Collections.IEnumerable)currentValue).Cast<IChangeTrackableInternal>().Select(v => v.GetCurrent());
                        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(property.PropertyType.GetGenericArguments().First()));
                        foreach (var item in originalValues)
                        {
                            list.Add(item);
                        }
                        currentValue = list;
                    }
                }
                property.SetValue(current, currentValue, null);
            }
            return current;
        }

        private bool Delete(object proxy)
        {
            if (_ChangeTrackingStatus != ChangeStatus.Deleted)
            {
                _ChangeTrackingStatus = ChangeStatus.Deleted;
                _StatusChanged(proxy, EventArgs.Empty);
                return true;
            }
            return false;
        }

        private bool UnDelete(object proxy)
        {
            if (_ChangeTrackingStatus == ChangeStatus.Deleted)
            {
                SetAndRaiseStatusChanged(proxy, true);
                return true;
            }
            return false;
        }

        private void AcceptChanges(object proxy)
        {
            if (_ChangeTrackingStatus == ChangeStatus.Deleted)
            {
                throw new InvalidOperationException("Can not call AcceptChanges on deleted object");
            }
            foreach (var child in GetChildren(proxy))
            {
                child.AcceptChanges();
            }
            _OriginalValueDictionary.Clear();
            SetAndRaiseStatusChanged(proxy, true);
        }

        private void RejectChanges(object proxy)
        {
            foreach (var child in GetChildren(proxy))
            {
                child.RejectChanges();
            }
            if (_OriginalValueDictionary.Count > 0)
            {
                _InRejectChanges = true;
                foreach (var changedProperty in _OriginalValueDictionary)
                {
                    _Properties[changedProperty.Key].SetValue(proxy, changedProperty.Value, null);
                }
                _OriginalValueDictionary.Clear();
                _InRejectChanges = false;
            }
            SetAndRaiseStatusChanged(proxy, true);
        }

        private static IEnumerable<System.ComponentModel.IRevertibleChangeTracking> GetChildren(object proxy)
        {
            return ((ICollectionPropertyTrackable)proxy).CollectionPropertyTrackables
                .Concat(((IComplexPropertyTrackable)proxy).ComplexPropertyTrackables)
                .OfType<System.ComponentModel.IRevertibleChangeTracking>();
        }


        private void UnsubscribeFromChildStatusChanged(string propertyName, object oldChild)
        {
            Delegate handler;
            if (_StatusChangedEventHandlers.TryGetValue(propertyName, out handler))
            {
                if (oldChild.DoIfType<IChangeTrackable>(trackable =>
                {
                    trackable.StatusChanged -= (EventHandler) handler;
                    _StatusChangedEventHandlers.Remove(propertyName);
                }))
                {
                    return;
                }
                oldChild.DoIfType<System.ComponentModel.IBindingList>(collectionTrackable =>
                {
                    collectionTrackable.ListChanged -= (System.ComponentModel.ListChangedEventHandler) handler;
                    _StatusChangedEventHandlers.Remove(propertyName);
                });
            }
        }

        private void SubscribeToChildStatusChanged(object proxy, string propertyName, object newValue)
        {
            if (!_StatusChangedEventHandlers.ContainsKey(propertyName))
            {
                if (newValue.DoIfType<IChangeTrackable>(newChild =>
                {
                    EventHandler newHandler = (sender, e) => SetAndRaiseStatusChanged(proxy, false);
                    newChild.StatusChanged += newHandler;
                    _StatusChangedEventHandlers.Add(propertyName, newHandler);
                }))
                {
                    return;
                }

                newValue.DoIfType<System.ComponentModel.IBindingList>(newCollectionChild =>
                {
                    System.ComponentModel.ListChangedEventHandler newHandler = (sender, e) => SetAndRaiseStatusChanged(proxy, false);
                    newCollectionChild.ListChanged += newHandler;
                    _StatusChangedEventHandlers.Add(propertyName, newHandler);
                });
            }
        }

        private void SetAndRaiseStatusChanged(object proxy, bool setStatusEvenIfStatsAddedOrDeleted)
        {
            if (_ChangeTrackingStatus == ChangeStatus.Changed || _ChangeTrackingStatus == ChangeStatus.Unchanged || setStatusEvenIfStatsAddedOrDeleted)
            {
                var newChangeStatus = GetNewChangeStatus(proxy);
                if (_ChangeTrackingStatus != newChangeStatus)
                {
                    _ChangeTrackingStatus = newChangeStatus;
                    _StatusChanged(proxy, EventArgs.Empty);
                }
            }
        }

        private ChangeStatus GetNewChangeStatus(object sender) => _OriginalValueDictionary.Count == 0 && GetChildren(sender).All(c => !c.IsChanged) ? ChangeStatus.Unchanged : ChangeStatus.Changed;

        private IEnumerable<string> GetChangedProperties()
        {
            switch (_ChangeTrackingStatus)
            {
                case ChangeStatus.Unchanged:
                    return Enumerable.Empty<string>();
                case ChangeStatus.Added:
                case ChangeStatus.Deleted:
                    return _Properties.Keys;
                case ChangeStatus.Changed:
                    return _OriginalValueDictionary.Keys;
                default: throw null;
            }
        }
    }
}
