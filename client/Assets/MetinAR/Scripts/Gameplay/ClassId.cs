using System;

namespace MetinAR.Gameplay
{
    /// <summary>Character classes set by the owner (2026-10-21): wojownik, ninja, szaman, Slajter.</summary>
    public enum ClassId
    {
        Warrior = 0,
        Ninja = 1,
        Shaman = 2,
        Slajter = 3,
    }

    public static class ClassIds
    {
        /// <summary>Data id used in docs/design tables (classes_skills.json, items_starter.csv).</summary>
        public static string ToDataId(ClassId id)
        {
            switch (id)
            {
                case ClassId.Warrior: return "warrior";
                case ClassId.Ninja: return "ninja";
                case ClassId.Shaman: return "shaman";
                case ClassId.Slajter: return "slajter";
                default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        public static bool TryParse(string dataId, out ClassId id)
        {
            switch (dataId)
            {
                case "warrior": id = ClassId.Warrior; return true;
                case "ninja": id = ClassId.Ninja; return true;
                case "shaman": id = ClassId.Shaman; return true;
                case "slajter": id = ClassId.Slajter; return true;
                default: id = default(ClassId); return false;
            }
        }

        public static ClassId Parse(string dataId)
        {
            ClassId id;
            if (!TryParse(dataId, out id)) throw new FormatException("Unknown class id '" + dataId + "'.");
            return id;
        }
    }
}
