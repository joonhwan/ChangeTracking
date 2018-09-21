﻿using System; 
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ChangeTracking
{
    public static class Utils
    {
        public static bool IsSetter(this MethodInfo method)
        {
            return method.IsSpecialName && method.Name.StartsWith("set_", StringComparison.Ordinal);
        }

        public static bool IsGetter(this MethodInfo method)
        {
            return method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal);
        }

        public static bool IsProperty(this MethodInfo method)
        {
            return method.IsSpecialName && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal));
        }

        public static string PropertyName(this MethodInfo method)
        {
            return method.Name.StartsWith("set_") ? method.Name.Substring("set_".Length) : method.Name.Substring("get_".Length);
        }

        public static bool DoIfType<T>(this object obj, Action<T> action)
        {
            if (obj is T)
            {
                action?.Invoke((T)obj);
                return true;
            }

            return false;
        }

        public static bool DoIfType<T>(this object obj, Func<T, bool> predicator, Action<T> action)
        {
            if (obj is T && predicator?.Invoke((T)obj)==true)
            {
                action?.Invoke((T)obj);
                return true;
            }

            return false;
        }
    }
}
