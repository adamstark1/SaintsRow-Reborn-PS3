using System;
using System.IO;

namespace SaintsRowReborn.Hydra.DataTypes
{
    public class HydraFloat64 : IHydraItem
    {
        public double Value { get; set; }
        
        public HydraDataType DataType => HydraDataType.Float64;

        public void Serialize(Stream s)
        {
            byte[] bytes = BitConverter.GetBytes(Value);
            Array.Reverse(bytes);
            s.Write(bytes, 0, bytes.Length);
        }

        public void Deserialize(Stream s)
        {
            byte[] bytes = new byte[8];
            s.Read(bytes, 0, 8);
            Array.Reverse(bytes);
            Value = BitConverter.ToDouble(bytes, 0);
        }

        public string GetString() => Value.ToString();
        public string GetString(int indentLevel) => Value.ToString();
    }
}