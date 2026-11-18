using Game.Data;
using Game.Domain;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Pooled visual for one item: a tinted placeholder tile, the item name, its level and, for generators,
    /// a charge badge with a recharge countdown. Holds no game state; the engine is the source of truth.
    /// </summary>
    internal sealed class ItemView : MonoBehaviour
    {
        private static readonly string[] SmallNumbers = BuildNumbers(64);

        private SpriteRenderer _body;
        private SpriteRenderer _ring;
        private TextMesh _name;
        private TextMesh _level;
        private TextMesh _charges;
        private TextMesh _timer;
        private int _shownCharges = -1;
        private string _shownTimer;

        public ItemId Id { get; private set; }
        public Location Home { get; set; }
        public bool IsGenerator { get; private set; }
        public Item Item { get; private set; }

        public static ItemView Create(Transform parent, Material spriteMaterial)
        {
            var go = new GameObject("Item");
            go.transform.SetParent(parent, false);
            ItemView view = go.AddComponent<ItemView>();
            view._ring = AddSprite(go.transform, "Ring", PlaceholderArt.Rounded, spriteMaterial, 1.0f);
            view._body = AddSprite(go.transform, "Body", PlaceholderArt.Rounded, spriteMaterial, 0.9f);
            // Sizes are fractions of the cell: at a 64 dp cell the name is about 10 dp tall.
            view._name = AddText(go.transform, "Name", new Vector3(0f, -0.05f, 0f), 0.15f, TextAnchor.MiddleCenter);
            view._level = AddText(go.transform, "Level", new Vector3(-0.3f, 0.3f, 0f), 0.17f, TextAnchor.MiddleCenter);
            view._charges = AddText(go.transform, "Charges", new Vector3(0.3f, 0.3f, 0f), 0.17f, TextAnchor.MiddleCenter);
            view._timer = AddText(go.transform, "Timer", new Vector3(0f, -0.33f, 0f), 0.14f, TextAnchor.MiddleCenter);
            go.SetActive(false);
            return view;
        }

        public void Bind(Item item, ItemCatalog catalog)
        {
            Item = item;
            Id = item.Id;
            Home = item.Location;
            IsGenerator = item.IsGenerator;
            Color chain = PlaceholderArt.ParseColor(catalog.GetColor(item.Type.ChainId), new Color(0.8f, 0.8f, 0.8f));
            _body.color = Color.Lerp(Color.white, chain, IsGenerator ? 0.95f : 0.55f + 0.05f * item.Type.Tier);
            _ring.color = IsGenerator ? new Color(0.25f, 0.2f, 0.15f, 0.9f) : new Color(0f, 0f, 0f, 0.12f);
            _name.text = Wrap(catalog.GetName(item.Type));
            _level.text = SmallNumbers[Mathf.Clamp(item.Type.Tier, 0, SmallNumbers.Length - 1)];
            _charges.gameObject.SetActive(IsGenerator);
            _timer.gameObject.SetActive(false);
            _shownCharges = -1;
            _shownTimer = null;
            transform.localScale = Vector3.one;
            gameObject.name = "Item " + item.Id;
            gameObject.SetActive(true);
        }

        /// <summary>Updates the generator badge. Text is only reassigned when it changes (no per-frame allocation).</summary>
        public void ShowCharges(int charges, string timer)
        {
            if (charges != _shownCharges)
            {
                _shownCharges = charges;
                _charges.text = SmallNumbers[Mathf.Clamp(charges, 0, SmallNumbers.Length - 1)];
                _charges.color = charges > 0 ? new Color(0.15f, 0.45f, 0.2f) : new Color(0.7f, 0.25f, 0.2f);
            }

            bool showTimer = timer != null;
            if (_timer.gameObject.activeSelf != showTimer)
            {
                _timer.gameObject.SetActive(showTimer);
            }

            if (showTimer && !ReferenceEquals(timer, _shownTimer))
            {
                _shownTimer = timer;
                _timer.text = timer;
            }
        }

        public void SetSorting(int baseOrder)
        {
            _ring.sortingOrder = baseOrder;
            _body.sortingOrder = baseOrder + 1;
            _name.GetComponent<MeshRenderer>().sortingOrder = baseOrder + 2;
            _level.GetComponent<MeshRenderer>().sortingOrder = baseOrder + 2;
            _charges.GetComponent<MeshRenderer>().sortingOrder = baseOrder + 2;
            _timer.GetComponent<MeshRenderer>().sortingOrder = baseOrder + 2;
        }

        public void ReleaseToPool()
        {
            Item = null;
            Id = ItemId.None;
            gameObject.SetActive(false);
        }

        private static string Wrap(string name)
        {
            if (name.Length <= 10)
            {
                return name;
            }

            int split = name.LastIndexOf(' ', Mathf.Min(name.Length - 1, 11));
            return split > 0 ? name.Substring(0, split) + "\n" + name.Substring(split + 1) : name;
        }

        private static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, Material material, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            return renderer;
        }

        internal static TextMesh AddText(Transform parent, string name, Vector3 localPosition, float lineHeight, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            TextMesh text = go.AddComponent<TextMesh>();
            text.font = PlaceholderArt.Font;
            text.fontSize = 64;
            // TextMesh line height is roughly fontSize * characterSize / 10 world units.
            text.characterSize = lineHeight * 10f / text.fontSize;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.2f, 0.16f, 0.12f);
            go.GetComponent<MeshRenderer>().sharedMaterial = PlaceholderArt.Font.material;
            return text;
        }

        private static string[] BuildNumbers(int count)
        {
            var numbers = new string[count];
            for (int i = 0; i < count; i++)
            {
                numbers[i] = i.ToString();
            }

            return numbers;
        }
    }

    /// <summary>Reuses item views so merges and spawns do not create or destroy GameObjects.</summary>
    internal sealed class ItemViewPool
    {
        private readonly System.Collections.Generic.Stack<ItemView> _free = new System.Collections.Generic.Stack<ItemView>();
        private readonly Transform _parent;
        private readonly Material _material;

        public ItemViewPool(Transform parent, Material material, int prewarm)
        {
            _parent = parent;
            _material = material;
            for (int i = 0; i < prewarm; i++)
            {
                _free.Push(ItemView.Create(_parent, _material));
            }
        }

        public ItemView Get() => _free.Count > 0 ? _free.Pop() : ItemView.Create(_parent, _material);

        public void Release(ItemView view)
        {
            view.ReleaseToPool();
            _free.Push(view);
        }
    }
}
