using System;
using UnityEngine;

namespace MetinAR.Presentation
{
    /// <summary>Explicit build-time references: no AssetDatabase or remote downloads in the player.</summary>
    [CreateAssetMenu(menuName = "MetinAR/Presentation/Art catalog")]
    public sealed class GameArtCatalog : ScriptableObject
    {
        public const string ResourceName = "MetinARPresentation";

        [SerializeField] private GameObject _fppArms;
        [SerializeField] private GameObject _wanderingMerchant;
        [SerializeField] private GameObject _starterBuckler;
        [SerializeField] private ItemArt[] _items = Array.Empty<ItemArt>();

        public GameObject FppArms => _fppArms;
        public GameObject WanderingMerchant => _wanderingMerchant;
        public GameObject StarterBuckler => _starterBuckler;
        public ItemArt[] Items => _items;

        public static GameArtCatalog Load() => Resources.Load<GameArtCatalog>(ResourceName);

        public ItemArt Find(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId)) return null;
            foreach (ItemArt item in _items)
                if (item != null && string.Equals(item.DefinitionId, definitionId, StringComparison.Ordinal))
                    return item;
            return null;
        }
    }

    [Serializable]
    public sealed class ItemArt
    {
        [SerializeField] private string _definitionId;
        [SerializeField] private string _displayName;
        [SerializeField] private string _description;
        [SerializeField] private Sprite _icon;
        [SerializeField] private GameObject _modelPrefab;

        public string DefinitionId => _definitionId;
        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public GameObject ModelPrefab => _modelPrefab;
    }
}
