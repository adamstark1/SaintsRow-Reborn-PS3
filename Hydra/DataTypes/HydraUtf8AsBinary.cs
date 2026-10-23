using System.Text;

namespace SaintsRowReborn.Hydra.DataTypes
{
    public class HydraUtf8StringAsBinary : HydraBinary
    {
        public HydraUtf8StringAsBinary(string value)
        {
            Value = Encoding.UTF8.GetBytes(value);
        }
    }
}