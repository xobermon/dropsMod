using System.Collections.Generic;
using System.Text;

namespace DropMod
{
    public class DropType : OctDatGlobal
    {
        public string name;
        public DropBehavior prefab;
        public IItemFilter filter;
        public float weight = 1f;

        public DropType(OctDatGlobalInitializer initializer) : base(initializer)
        {
            // don't add the type when we're just creating an instance of this type to know the defaults
            if (!initializer.forDefaults)
            {
                // otherwise register with the manager!
                DropManager.Instance.AddType(this);
            }
        }
    }
}
