using System.IO;

namespace SaintsRowReborn.Hydra.DataTypes
{
    public class HydraNone : IHydraItem
    {
        public HydraDataType DataType => HydraDataType.None;
        public void Serialize(Stream s) { }
        public void Deserialize(Stream s) { }
        public string GetString() => "None";
        public string GetString(int indentLevel) => "None";
    }
}