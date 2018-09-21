using System.Drawing;

namespace ConsoleApplication1
{
    public class MaterialInfo
    {
        public virtual string Name { get; set; }
        public virtual MaterialTypeEnum MaterialType { get; set; }
        public virtual Color Color { get; set; }
    }
}