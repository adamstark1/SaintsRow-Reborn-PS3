using System;
using System.IO;

namespace SaintsRowReborn.Hydra.DataTypes
{
    public class HydraDateTime : IHydraItem
    {
        public DateTime Value { get; set; }
        public HydraDateTime() { }
        public HydraDateTime(DateTime value) { Value = value; }

        public HydraDataType DataType => HydraDataType.DateTime;

        public void Serialize(Stream s)
        {
            s.WriteInt64(new DateTimeOffset(Value).ToUnixTimeSeconds().Swap());
        }

        public void Deserialize(Stream s)
        {
            long unixTime = s.ReadInt64().Swap();
            Value = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
        }

        public string GetString() => Value.ToString();
        public string GetString(int indentLevel) => Value.ToString();
    }
}