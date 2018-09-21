using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using Castle.DynamicProxy;
using ChangeTracking;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            DoTestDynamicProxy1();
            //ChangeTrackingTest();
        }

        private static void DoTestDynamicProxy1()
        {
            var generator = new ProxyGenerator();
            var proxy = generator.CreateClassProxyWithTarget<MaterialInfo>(new MaterialInfo() { Color = Color.Red, Name="Test", MaterialType = MaterialTypeEnum.Cauchy}, new LoggableInterceptor());
            proxy.Color = Color.Blue;
        }

        private static void ChangeTrackingTest()
        {
            var colors = Enum.GetValues(typeof(KnownColor));
            var types = Enum.GetValues(typeof(MaterialTypeEnum));
            var random = new Random();

            var materials = new BindingList<MaterialInfo>()(
                Enumerable.Range(0, 10).Select(i =>
                        new MaterialInfo
                        {
                            Name = $"Name_{i:000}",
                            Color = Color.FromKnownColor((KnownColor) colors.GetValue(random.Next(colors.Length))),
                            MaterialType = (MaterialTypeEnum) types.GetValue(random.Next(types.Length)),
                        }
                    )
                    .ToList()
            );

            if (materials is ICollection)
            {
                Console.WriteLine("Yes");
            }

            var trackableMaterials = materials.AsTrackable();

            var firstItem = trackableMaterials.FirstOrDefault();
            trackableMaterials.Remove(firstItem);
        }
    }

    public class LoggableInterceptor : StandardInterceptor
    {
        protected override void PerformProceed(IInvocation invocation)
        {
            base.PerformProceed(invocation);

            Console.WriteLine($"MethodName : {invocation.Method.Name}");
        }
    }
}
