﻿using Castle.DynamicProxy;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;

namespace ChangeTracking
{
    internal sealed class ChangeTrackingCollectionInterceptor<T> : IInterceptor, IChangeTrackableCollection<T>, IInterceptorSettings where T : class
    {
        private ChangeTrackingBindingList<T> _WrappedTarget;
        private IList<T> _DeletedItems;
        private static HashSet<string> _ImplementedMethods;
        private static HashSet<string> _BindingListImplementedMethods;
        private static HashSet<string> _IBindingListImplementedMethods;
        private static HashSet<string> _INotifyCollectionChangedImplementedMethods;
        private readonly bool _MakeComplexPropertiesTrackable;
        private readonly bool _MakeCollectionPropertiesTrackable;

        public bool IsInitialized { get; set; }

        static ChangeTrackingCollectionInterceptor()
        {
            _ImplementedMethods = new HashSet<string>(typeof(ChangeTrackingCollectionInterceptor<T>).GetMethods(BindingFlags.Instance | BindingFlags.Public).Select(m => m.Name));
            _BindingListImplementedMethods = new HashSet<string>(typeof(ChangeTrackingBindingList<T>).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy).Select(m => m.Name));
            _IBindingListImplementedMethods = new HashSet<string>(typeof(ChangeTrackingBindingList<T>).GetInterfaceMap(typeof(System.ComponentModel.IBindingList)).TargetMethods.Where(mi => mi.IsPrivate).Select(mi => mi.Name.Substring(mi.Name.LastIndexOf('.') + 1)));
            _INotifyCollectionChangedImplementedMethods = new HashSet<string>(typeof(INotifyCollectionChanged).GetMethods(BindingFlags.Instance | BindingFlags.Public).Select(m => m.Name));
        }

        internal ChangeTrackingCollectionInterceptor(IList<T> target, bool makeComplexPropertiesTrackable, bool makeCollectionPropertiesTrackable)
        {
            _MakeComplexPropertiesTrackable = makeComplexPropertiesTrackable;
            _MakeCollectionPropertiesTrackable = makeCollectionPropertiesTrackable;
            for (int i = 0; i < target.Count; i++)
            {
                target[i] = target[i].AsTrackable(ChangeStatus.Unchanged, ItemCanceled, _MakeComplexPropertiesTrackable, _MakeCollectionPropertiesTrackable);
            }
            _WrappedTarget = new ChangeTrackingBindingList<T>(target, DeleteItem, ItemCanceled, _MakeComplexPropertiesTrackable, _MakeCollectionPropertiesTrackable);
            _DeletedItems = new List<T>();
        }
        
        public void Intercept(IInvocation invocation)
        {
            if (_ImplementedMethods.Contains(invocation.Method.Name))
            {
                invocation.ReturnValue = invocation.Method.Invoke(this, invocation.Arguments);
                return;
            }
            if (_BindingListImplementedMethods.Contains(invocation.Method.Name))
            {
                invocation.ReturnValue = invocation.Method.Invoke(_WrappedTarget, invocation.Arguments);
                return;
            }
            if (_IBindingListImplementedMethods.Contains(invocation.Method.Name))
            {
                invocation.ReturnValue = invocation.Method.Invoke(_WrappedTarget, invocation.Arguments);
                return;
            }
            if (_INotifyCollectionChangedImplementedMethods.Contains(invocation.Method.Name))
            {
                invocation.ReturnValue = invocation.Method.Invoke(_WrappedTarget, invocation.Arguments);
                return;
            }
            invocation.Proceed();
        }

        private void DeleteItem(T item)
        {
            var currentStatus = item.CastToIChangeTrackable().ChangeTrackingStatus;
            var manager = (IChangeTrackingManager)item;
            bool deleteSuccess = manager.Delete();
            if (deleteSuccess && currentStatus != ChangeStatus.Added)
            {
                _DeletedItems.Add(item);
            }
        }

        private void ItemCanceled(T item) => _WrappedTarget.CancelNew(_WrappedTarget.IndexOf(item));

        public IEnumerable<T> UnchangedItems => _WrappedTarget.Cast<IChangeTrackable<T>>().Where(ct => ct.ChangeTrackingStatus == ChangeStatus.Unchanged).Cast<T>();

        public IEnumerable<T> AddedItems => _WrappedTarget.Cast<IChangeTrackable<T>>().Where(ct => ct.ChangeTrackingStatus == ChangeStatus.Added).Cast<T>();

        public IEnumerable<T> ChangedItems => _WrappedTarget.Cast<IChangeTrackable<T>>().Where(ct => ct.ChangeTrackingStatus == ChangeStatus.Changed).Cast<T>();

        public IEnumerable<T> DeletedItems => _DeletedItems.Select(i => i);

        public bool UnDelete(T item)
        {
            var manager = (IChangeTrackingManager)item;
            bool unDeleteSuccess = manager.UnDelete();
            if (unDeleteSuccess)
            {
                bool removeSuccess = _DeletedItems.Remove(item);
                if (removeSuccess)
                {
                    _WrappedTarget.Add(item);
                    return true;
                }
            }
            return false;
        }

        public void AcceptChanges()
        {
            foreach (var item in _WrappedTarget.Cast<IChangeTrackable<T>>())
            {
                item.AcceptChanges();
                item.DoIfType<System.ComponentModel.IEditableObject>(editable =>
                    editable.EndEdit()
                );

            }
            _DeletedItems.Clear();
        }

        public void RejectChanges()
        {
            AddedItems.ToList().ForEach(i => _WrappedTarget.Remove(i));
            foreach (var item in _WrappedTarget.Cast<IChangeTrackable<T>>())
            {
                item.RejectChanges();
            }
            foreach (var item in _DeletedItems)
            {
                ((System.ComponentModel.IRevertibleChangeTracking)item).RejectChanges();
                _WrappedTarget.Add(item);
            }
            _DeletedItems.Clear();
        }

        public bool IsChanged => ChangedItems.Any() || AddedItems.Any() || DeletedItems.Any();

        public IEnumerator<T> GetEnumerator() => _WrappedTarget.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
