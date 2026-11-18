using System;
using System.Collections.Generic;
using Game.Data;
using Game.Domain;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// M2 playable merge board. Loads validated content, owns the engine for this session, lays the board out
    /// for the current screen and safe area, turns pointer input into engine commands via
    /// <see cref="BoardPresenter"/>, and mirrors engine events into pooled placeholder views.
    ///
    /// Rules followed here:
    /// - Views never change game state; every change is an engine command, and refused commands change nothing.
    /// - Engine event handlers only update visuals; they never issue engine commands.
    /// - Drag is visual only. Pausing or losing focus mid-drag returns the item, so nothing is lost or duplicated.
    /// - No per-frame allocations while dragging (positions, cached strings, fixed tween array).
    ///
    /// Not in M2 (later milestones): saving (M4), energy, coins and orders (M3), audio and haptics.
    /// </summary>
    public sealed class BoardScreen : MonoBehaviour
    {
        private const int CellOrder = 0;
        private const int HighlightOrder = 1;
        private const int ItemOrder = 10;
        private const int HeldOrder = 100;
        private const float MoveSeconds = 0.18f;
        private const float MergeSeconds = 0.16f;
        private const float BounceSeconds = 0.22f;
        private const float ShakeSeconds = 0.32f;

        [SerializeField] private LayoutSpec layoutSpec = new LayoutSpec();
        [Tooltip("Unlit sprite material (assigned by Tools > The Life We Left Behind > Create Merge Board Scene). Optional.")]
        [SerializeField] private Material spriteMaterial = null;
        [SerializeField] private string contentResource = "MergeContent";
        [SerializeField] private Color backgroundColor = new Color(0.96f, 0.92f, 0.84f);
        [Tooltip("Shows the measured cell size in dp/px (Editor and development builds only).")]
        [SerializeField] private bool showCellSizeReport = true;
        [SerializeField] private float hintDelaySeconds = 8f;
        [SerializeField] private float messageSeconds = 2.5f;

        private readonly Dictionary<long, ItemView> _views = new Dictionary<long, ItemView>();
        private readonly List<ItemView> _generatorViews = new List<ItemView>();

        private MergeContent _content;
        private Ledger _ledger;
        private Camera _camera;
        private Transform _root;
        private Tweener _tweener;
        private ItemViewPool _pool;
        private DragGesture _gesture;

        private SpriteRenderer[] _cellTiles;
        private SpriteRenderer[] _cellHighlights;
        private TextMesh[] _lockLabels;
        private SpriteRenderer[] _slotTiles;
        private SpriteRenderer _sellTile;
        private TextMesh _sellLabel;
        private TextMesh _storageLabel;
        private TextMesh _header;
        private TextMesh _report;

        private int _screenWidth;
        private int _screenHeight;
        private Rect _safeArea;
        private int _slotCountShown = -1;
        private int _headerMaxChars = 40;

        // Gesture state.
        private ItemView _pressedView;
        private Location _pressedAt;
        private ItemView _held;
        private Location _heldFrom;
        private int _highlightedCell = -1;
        private HoverKind _highlightKind;
        private DropZone _highlightZone;

        // Context for engine events raised during the current command.
        private int _spawnOriginCell = -1;
        private int _mergeTargetCell = -1;

        private float _messageHideAt;
        private float _nextBadgeRefresh;
        private float _lastInteraction;
        private float _nextHintAt;

        public MergeEngine Engine { get; private set; }
        public BoardPresenter Presenter { get; private set; }
        public BoardLayout Layout { get; private set; }
        /// <summary>Content error shown instead of a board, or null when the board loaded.</summary>
        public string LoadError { get; private set; }
        public int ViewCount => _views.Count;

        /// <summary>Wall-clock time for generator recharge. Saved timestamps (M4) will use the same clock.</summary>
        private static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Application.targetFrameRate = 60;
            _root = new GameObject("Board Root").transform;
            _root.SetParent(transform, false);
            _tweener = gameObject.AddComponent<Tweener>();
            _tweener.Released = OnShrinkFinished;
            SetUpCamera();
            _header = ItemView.AddText(_root, "Header", Vector3.zero, 1f, TextAnchor.MiddleCenter);
            _header.GetComponent<MeshRenderer>().sortingOrder = HeldOrder + 10;

            if (!LoadContent())
            {
                _header.text = "Content error (see Console):\n" + LoadError;
                _header.color = new Color(0.7f, 0.15f, 0.1f);
                RelayoutIfScreenChanged(true);
                return;
            }

            _ledger = Debug.isDebugBuild ? new Ledger() : null;
            Engine = MergeEngine.CreateNew(_content.Config, (ulong)DateTime.UtcNow.Ticks, _content.StartingLayout, _ledger);
            Presenter = new BoardPresenter(Engine, _content.Catalog);
            _pool = new ItemViewPool(_root, spriteMaterial, Engine.CellCount + Engine.Config.Board.MaxInventoryCapacity);
            _gesture = new DragGesture(10f);

            BuildStaticViews();
            Engine.ItemCreated += OnItemCreated;
            Engine.ItemRemoved += OnItemRemoved;
            Engine.ItemMoved += OnItemMoved;
            Engine.InventoryCapacityChanged += OnInventoryCapacityChanged;

            var items = new List<Item>(Engine.ItemCount);
            Engine.CopyItems(items);
            foreach (Item item in items)
            {
                OnItemCreated(item, CreationReason.Load);
            }

            RelayoutIfScreenChanged(true);
            _lastInteraction = Time.unscaledTime;
            ShowMessage("Drag matching items together to merge. Tap a toolbox to make new items.");
        }

        private void OnDestroy()
        {
            if (Engine != null)
            {
                Engine.ItemCreated -= OnItemCreated;
                Engine.ItemRemoved -= OnItemRemoved;
                Engine.ItemMoved -= OnItemMoved;
                Engine.InventoryCapacityChanged -= OnInventoryCapacityChanged;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                CancelGesture();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                CancelGesture();
            }
        }

        private void Update()
        {
            RelayoutIfScreenChanged(false);
            if (Engine == null)
            {
                return;
            }

            HandlePointer();

            float now = Time.unscaledTime;
            if (now >= _nextBadgeRefresh)
            {
                _nextBadgeRefresh = now + 0.25f;
                RefreshGeneratorBadges();
            }

            if (_messageHideAt > 0f && now >= _messageHideAt)
            {
                _messageHideAt = 0f;
                _header.text = string.Empty;
            }

            if (_held == null && now - _lastInteraction >= hintDelaySeconds && now >= _nextHintAt)
            {
                _nextHintAt = now + 3f;
                ShowHint();
            }
        }

        // ------------------------------------------------------------------ setup

        private bool LoadContent()
        {
            TextAsset asset = Resources.Load<TextAsset>(contentResource);
            if (asset == null)
            {
                LoadError = "Missing Resources/" + contentResource + ".json";
                Debug.LogError("[Board] " + LoadError);
                return false;
            }

            MergeContentData data;
            try
            {
                data = JsonUtility.FromJson<MergeContentData>(asset.text);
            }
            catch (ArgumentException e)
            {
                LoadError = "Content JSON could not be parsed: " + e.Message;
                Debug.LogError("[Board] " + LoadError);
                return false;
            }

            if (!MergeContentMapper.TryBuild(data, out _content, out List<string> errors))
            {
                LoadError = string.Join("\n", errors);
                Debug.LogError("[Board] Content rejected:\n" + LoadError);
                return false;
            }

            return true;
        }

        private void SetUpCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = go.AddComponent<Camera>();
            }

            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backgroundColor;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;
        }

        private void BuildStaticViews()
        {
            int cells = Engine.CellCount;
            _cellTiles = new SpriteRenderer[cells];
            _cellHighlights = new SpriteRenderer[cells];
            _lockLabels = new TextMesh[cells];
            for (int i = 0; i < cells; i++)
            {
                _cellTiles[i] = AddTile("Cell " + i, CellOrder, new Color(1f, 0.98f, 0.94f));
                _cellHighlights[i] = AddTile("Highlight " + i, HighlightOrder, Color.clear);
                _lockLabels[i] = ItemView.AddText(_cellTiles[i].transform, "Lock", Vector3.zero, 0.16f, TextAnchor.MiddleCenter);
                _lockLabels[i].text = "Locked";
                _lockLabels[i].color = new Color(0.45f, 0.4f, 0.35f);
                _lockLabels[i].GetComponent<MeshRenderer>().sortingOrder = CellOrder + 1;
            }

            int maxSlots = Engine.Config.Board.MaxInventoryCapacity;
            _slotTiles = new SpriteRenderer[maxSlots];
            for (int s = 0; s < maxSlots; s++)
            {
                _slotTiles[s] = AddTile("Slot " + s, CellOrder, new Color(0.9f, 0.85f, 0.76f));
            }

            _sellTile = AddTile("Sell", CellOrder, new Color(0.93f, 0.78f, 0.7f));
            _sellLabel = ItemView.AddText(_sellTile.transform, "Label", Vector3.zero, 0.22f, TextAnchor.MiddleCenter);
            _sellLabel.text = "Sell";
            _sellLabel.GetComponent<MeshRenderer>().sortingOrder = CellOrder + 1;
            _storageLabel = ItemView.AddText(_root, "Storage", Vector3.zero, 1f, TextAnchor.LowerLeft);
            _storageLabel.text = "Storage";
            _storageLabel.color = new Color(0.45f, 0.38f, 0.3f);

            _report = ItemView.AddText(_root, "Cell size report", Vector3.zero, 1f, TextAnchor.UpperLeft);
            _report.color = new Color(0.35f, 0.3f, 0.25f, 0.85f);
            _report.alignment = TextAlignment.Left;
            _report.gameObject.SetActive(showCellSizeReport && Debug.isDebugBuild);
        }

        private SpriteRenderer AddTile(string name, int order, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderArt.Rounded;
            if (spriteMaterial != null)
            {
                renderer.sharedMaterial = spriteMaterial;
            }

            renderer.sortingOrder = order;
            renderer.color = color;
            return renderer;
        }

        // ------------------------------------------------------------------ layout

        private void RelayoutIfScreenChanged(bool force)
        {
            Rect safe = Screen.safeArea;
            if (!force && Screen.width == _screenWidth && Screen.height == _screenHeight && safe == _safeArea &&
                (Engine == null || Engine.InventoryCapacity == _slotCountShown))
            {
                return;
            }

            CancelGesture();
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            _safeArea = safe;

            // One world unit = one screen pixel, origin at the bottom-left.
            _camera.orthographicSize = _screenHeight * 0.5f;
            _camera.transform.position = new Vector3(_screenWidth * 0.5f, _screenHeight * 0.5f, -10f);

            if (Engine == null)
            {
                _header.transform.position = new Vector3(_screenWidth * 0.5f, _screenHeight * 0.5f, 0f);
                SetTextHeight(_header, Mathf.Max(14f, _screenWidth / 40f));
                return;
            }

            var metrics = new ScreenMetrics(_screenWidth, _screenHeight, Screen.dpi, safe.x, safe.y, safe.width, safe.height);
            Layout = new BoardLayout(metrics, layoutSpec, Engine.Columns, Engine.Rows, Engine.InventoryCapacity);
            _gesture.ThresholdPx = Layout.DragThresholdPx;
            _slotCountShown = Engine.InventoryCapacity;

            float cell = Layout.CellPx;
            for (int i = 0; i < Engine.CellCount; i++)
            {
                PlaceTile(_cellTiles[i], Layout.CellCentreX(i), Layout.CellCentreY(i), cell * 0.96f);
                PlaceTile(_cellHighlights[i], Layout.CellCentreX(i), Layout.CellCentreY(i), cell * 0.96f);
                bool locked = Engine.IsLocked(i);
                _cellTiles[i].color = locked ? new Color(0.82f, 0.77f, 0.7f) : new Color(1f, 0.98f, 0.94f);
                _lockLabels[i].gameObject.SetActive(locked);
            }

            for (int s = 0; s < _slotTiles.Length; s++)
            {
                bool active = s < Engine.InventoryCapacity;
                _slotTiles[s].gameObject.SetActive(active);
                if (active)
                {
                    PlaceTile(_slotTiles[s], Layout.SlotCentreX(s), Layout.SlotY, Layout.SlotPx);
                }
            }

            PlaceTile(_sellTile, Layout.SellX, Layout.SlotY, Layout.SlotPx);
            _storageLabel.transform.position = new Vector3(Layout.SlotLeft, Layout.SlotY + Layout.SlotPx * 0.55f, 0f);
            SetTextHeight(_storageLabel, Layout.SlotPx * 0.22f);

            _header.transform.position = new Vector3(Layout.HeaderX, Layout.HeaderY, 0f);
            float headerLine = Mathf.Max(12f, cell * 0.22f);
            SetTextHeight(_header, headerLine);
            // Legacy font glyphs average roughly half the line height in width.
            _headerMaxChars = Mathf.Max(12, (int)(safe.width * 0.9f / (headerLine * 0.5f)));

            foreach (ItemView view in _views.Values)
            {
                _tweener.Stop(view.transform);
                view.transform.position = HomePosition(view.Home);
                view.transform.localScale = HomeScale(view.Home);
            }

            UpdateCellSizeReport(safe);
        }

        private void UpdateCellSizeReport(Rect safe)
        {
            string report =
                "Cell " + Layout.CellDp.ToString("0") + " dp (" + Layout.CellPx.ToString("0") + " px)  " + Engine.Columns + "x" + Engine.Rows +
                (Layout.HeightLimited ? "  height-limited" : "  width-limited") +
                (Layout.BelowComfort ? "  BELOW " + layoutSpec.minComfortCellDp.ToString("0") + " dp" : "") +
                "\nScreen " + _screenWidth + "x" + _screenHeight + " px, " +
                (Layout.UsedFallbackDpi ? "dpi unknown (fallback)" : Screen.dpi.ToString("0") + " dpi") +
                ", safe " + safe.width.ToString("0") + "x" + safe.height.ToString("0") + " at " + safe.x.ToString("0") + "," + safe.y.ToString("0");
            Debug.Log("[Board] " + report.Replace('\n', ' '));
            if (_report.gameObject.activeSelf)
            {
                _report.text = report;
                SetTextHeight(_report, Mathf.Max(10f, Layout.CellPx * 0.12f));
                _report.transform.position = new Vector3(safe.x + 8f, safe.y + safe.height - 8f, 0f);
            }
        }

        private static void PlaceTile(SpriteRenderer tile, float x, float y, float size)
        {
            tile.transform.position = new Vector3(x, y, 0f);
            tile.transform.localScale = new Vector3(size, size, 1f);
        }

        private static void SetTextHeight(TextMesh text, float lineHeightPx) =>
            text.characterSize = lineHeightPx * 10f / text.fontSize;

        private Vector3 HomePosition(Location at) =>
            at.IsInventory
                ? new Vector3(Layout.SlotCentreX(at.Index), Layout.SlotY, 0f)
                : new Vector3(Layout.CellCentreX(at.Index), Layout.CellCentreY(at.Index), 0f);

        private Vector3 HomeScale(Location at)
        {
            float size = at.IsInventory ? Layout.SlotPx * 0.92f : Layout.CellPx * 0.92f;
            return new Vector3(size, size, 1f);
        }

        // ------------------------------------------------------------------ input

        private void HandlePointer()
        {
            if (!PointerInput.TryRead(out Vector2 position, out bool pressed))
            {
                return;
            }

            if (pressed && !_gesture.IsPressed)
            {
                OnPress(position);
            }
            else if (pressed)
            {
                if (_gesture.Move(position.x, position.y) == GestureEvent.DragStarted)
                {
                    BeginDrag();
                }

                if (_held != null)
                {
                    UpdateDrag(position);
                }
            }
            else if (_gesture.IsPressed)
            {
                GestureEvent ended = _gesture.Release();
                if (ended == GestureEvent.Tap)
                {
                    OnTap();
                }
                else if (ended == GestureEvent.Drop)
                {
                    OnDrop(position);
                }

                _pressedView = null;
            }
        }

        private void OnPress(Vector2 position)
        {
            _gesture.Press(position.x, position.y);
            _pressedView = null;
            int cell = Layout.CellAt(position.x, position.y);
            Item item = null;
            if (cell >= 0)
            {
                item = Engine.GetCell(cell);
                _pressedAt = Location.Cell(cell);
            }
            else
            {
                int slot = Layout.SlotAt(position.x, position.y);
                if (slot >= 0)
                {
                    item = Engine.GetSlot(slot);
                    _pressedAt = Location.Slot(slot);
                }
            }

            if (item != null)
            {
                _views.TryGetValue(item.Id.Value, out _pressedView);
            }
        }

        private void BeginDrag()
        {
            if (_pressedView == null)
            {
                return;
            }

            _held = _pressedView;
            _heldFrom = _pressedAt;
            _tweener.Stop(_held.transform);
            _held.SetSorting(HeldOrder);
            _tweener.ScaleTo(_held.transform, HomeScale(_heldFrom).x * 1.12f, 0.1f); // pick-up
            MarkInteraction();
        }

        private void UpdateDrag(Vector2 position)
        {
            // Lift the item slightly above the finger so it stays visible.
            _held.transform.position = new Vector3(position.x, position.y + Layout.CellPx * 0.18f, 0f);
            PointerTarget target = TargetAt(position);
            HoverKind hover = Presenter.Hover(_heldFrom, _held.Id, target);
            SetHighlight(target, hover);
        }

        private PointerTarget TargetAt(Vector2 position)
        {
            int cell = Layout.CellAt(position.x, position.y);
            if (cell >= 0)
            {
                return PointerTarget.AtCell(cell);
            }

            if (Layout.IsOverSell(position.x, position.y))
            {
                return PointerTarget.Sell;
            }

            return Layout.IsOverInventory(position.x, position.y) ? PointerTarget.Inventory : PointerTarget.Nothing;
        }

        private void OnDrop(Vector2 position)
        {
            if (_held == null)
            {
                return;
            }

            ItemView held = _held;
            Location from = _heldFrom;
            PointerTarget target = TargetAt(position);
            ClearHighlight();
            _held = null;

            _mergeTargetCell = target.Zone == DropZone.Cell ? target.Cell : -1;
            InteractionResult result = Presenter.Drop(from, held.Id, target);
            _mergeTargetCell = -1;

            held.SetSorting(ItemOrder);
            switch (result.Kind)
            {
                case InteractionKind.Bounced:
                    _tweener.Move(held.transform, HomePosition(from), BounceSeconds);
                    _tweener.ScaleTo(held.transform, HomeScale(from).x, BounceSeconds);
                    if (result.InvalidFeedback)
                    {
                        PlayInvalid(result.Cell);
                    }

                    break;
                case InteractionKind.Sold:
                    break; // the removal event animates it out
                case InteractionKind.Merged:
                    break; // removal and creation events animate it
                default:
                    if (held.Item != null)
                    {
                        _tweener.ScaleTo(held.transform, HomeScale(held.Home).x, MoveSeconds);
                    }

                    break;
            }

            if (result.Message != null)
            {
                ShowMessage(result.Message);
            }

            AfterCommand();
        }

        private void OnTap()
        {
            if (_pressedView == null)
            {
                return;
            }

            ItemView view = _pressedView;
            _spawnOriginCell = _pressedAt.IsBoard ? _pressedAt.Index : -1;
            InteractionResult result = Presenter.Tap(_pressedAt, view.Id, NowMs);
            _spawnOriginCell = -1;

            switch (result.Kind)
            {
                case InteractionKind.Spawned:
                    _tweener.Pop(view.transform, HomeScale(view.Home).x * 0.88f, HomeScale(view.Home).x, 0.2f);
                    RefreshGeneratorBadges();
                    break;
                case InteractionKind.BoardFull:
                case InteractionKind.GeneratorEmpty:
                    _tweener.Shake(view.transform, Layout.CellPx * 0.06f, ShakeSeconds);
                    break;
                case InteractionKind.Info:
                    _tweener.Pop(view.transform, HomeScale(view.Home).x * 0.94f, HomeScale(view.Home).x, 0.18f);
                    break;
            }

            if (result.Message != null)
            {
                ShowMessage(result.Message);
            }

            MarkInteraction();
            AfterCommand();
        }

        /// <summary>Abandons the current gesture: a held item returns home. Nothing in the engine has changed.</summary>
        private void CancelGesture()
        {
            if (_gesture == null || _gesture.Cancel() == GestureEvent.None)
            {
                return;
            }

            ClearHighlight();
            if (_held != null && _held.Item != null)
            {
                _held.SetSorting(ItemOrder);
                _tweener.Stop(_held.transform);
                _held.transform.position = HomePosition(_held.Home);
                _held.transform.localScale = HomeScale(_held.Home);
            }

            _held = null;
            _pressedView = null;
        }

        // ------------------------------------------------------------------ highlights and feedback

        private void SetHighlight(PointerTarget target, HoverKind kind)
        {
            int cell = target.Zone == DropZone.Cell ? target.Cell : -1;
            if (cell == _highlightedCell && kind == _highlightKind && target.Zone == _highlightZone)
            {
                return;
            }

            ClearHighlight();
            _highlightedCell = cell;
            _highlightKind = kind;
            _highlightZone = target.Zone;
            Color color = HighlightColor(kind);
            if (cell >= 0)
            {
                _cellHighlights[cell].color = color;
            }
            else if (target.Zone == DropZone.Sell)
            {
                _sellTile.color = kind == HoverKind.Invalid ? new Color(0.85f, 0.6f, 0.55f) : new Color(0.98f, 0.7f, 0.55f);
            }
            else if (target.Zone == DropZone.Inventory)
            {
                for (int s = 0; s < Engine.InventoryCapacity; s++)
                {
                    _slotTiles[s].color = kind == HoverKind.Invalid ? new Color(0.88f, 0.72f, 0.66f) : new Color(0.84f, 0.9f, 0.75f);
                }
            }
        }

        private void ClearHighlight()
        {
            if (_highlightedCell >= 0)
            {
                _cellHighlights[_highlightedCell].color = Color.clear;
            }

            if (_highlightZone == DropZone.Sell && _sellTile != null)
            {
                _sellTile.color = new Color(0.93f, 0.78f, 0.7f);
            }

            if (_highlightZone == DropZone.Inventory && _slotTiles != null)
            {
                for (int s = 0; s < _slotTiles.Length; s++)
                {
                    _slotTiles[s].color = new Color(0.9f, 0.85f, 0.76f);
                }
            }

            _highlightedCell = -1;
            _highlightKind = HoverKind.None;
            _highlightZone = DropZone.None;
        }

        private static Color HighlightColor(HoverKind kind)
        {
            switch (kind)
            {
                case HoverKind.Merge: return new Color(0.55f, 0.85f, 0.45f, 0.75f);
                case HoverKind.Move: return new Color(0.6f, 0.75f, 0.95f, 0.45f);
                case HoverKind.Invalid: return new Color(0.95f, 0.55f, 0.45f, 0.45f);
                case HoverKind.Return: return new Color(0.7f, 0.7f, 0.7f, 0.25f);
                default: return Color.clear;
            }
        }

        /// <summary>Subtle "invalid" response: a short wobble of whatever sits at the target cell.</summary>
        private void PlayInvalid(int cell)
        {
            if (cell < 0 || cell >= Engine.CellCount)
            {
                return;
            }

            Item item = Engine.GetCell(cell);
            if (item != null && _views.TryGetValue(item.Id.Value, out ItemView view))
            {
                _tweener.Shake(view.transform, Layout.CellPx * 0.05f, ShakeSeconds);
            }
        }

        private void ShowMessage(string message)
        {
            _header.text = TextWrap.Wrap(message, _headerMaxChars);
            _header.color = new Color(0.3f, 0.24f, 0.18f);
            _messageHideAt = Time.unscaledTime + messageSeconds;
        }

        private void ShowHint()
        {
            if (!Engine.TryFindMerge(out MergeHint hint))
            {
                return;
            }

            PulseCell(hint.CellA);
            PulseCell(hint.CellB);
        }

        private void PulseCell(int cell)
        {
            Item item = Engine.GetCell(cell);
            if (item != null && _views.TryGetValue(item.Id.Value, out ItemView view))
            {
                float scale = HomeScale(view.Home).x;
                _tweener.Pop(view.transform, scale * 1.1f, scale, 0.45f);
            }
        }

        private void MarkInteraction()
        {
            _lastInteraction = Time.unscaledTime;
            _nextHintAt = 0f;
        }

        private void RefreshGeneratorBadges()
        {
            long now = NowMs;
            for (int i = 0; i < _generatorViews.Count; i++)
            {
                ItemView view = _generatorViews[i];
                if (view.Item == null)
                {
                    continue;
                }

                int charges = Engine.GetAvailableCharges(view.Item, now);
                long next = Engine.GetNextChargeInMs(view.Item, now);
                view.ShowCharges(charges, next > 0 && charges == 0 ? TimeText.Format(next) : null);
            }
        }

        /// <summary>Development safety net: verify the engine invariants after every player command.</summary>
        private void AfterCommand()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            List<string> violations = InvariantChecker.Check(Engine, _ledger);
            if (violations.Count > 0)
            {
                Debug.LogError("[Board] Invariant violation:\n" + string.Join("\n", violations));
            }
#endif
        }

        // ------------------------------------------------------------------ engine events (visuals only)

        private void OnItemCreated(Item item, CreationReason reason)
        {
            ItemView view = _pool.Get();
            view.Bind(item, _content.Catalog);
            view.SetSorting(ItemOrder);
            _views[item.Id.Value] = view;
            if (view.IsGenerator)
            {
                _generatorViews.Add(view);
            }

            if (Layout == null)
            {
                return; // first layout positions everything
            }

            Vector3 home = HomePosition(item.Location);
            float scale = HomeScale(item.Location).x;
            if (reason == CreationReason.Spawn && _spawnOriginCell >= 0)
            {
                view.transform.position = new Vector3(Layout.CellCentreX(_spawnOriginCell), Layout.CellCentreY(_spawnOriginCell), 0f);
                view.transform.localScale = new Vector3(scale * 0.4f, scale * 0.4f, 1f);
                _tweener.Move(view.transform, home, 0.28f);
                _tweener.ScaleTo(view.transform, scale, 0.28f);
            }
            else if (reason == CreationReason.Merge)
            {
                view.transform.position = home;
                _tweener.Pop(view.transform, scale * 0.6f, scale, 0.3f);
            }
            else
            {
                view.transform.position = home;
                view.transform.localScale = new Vector3(scale, scale, 1f);
            }

            view.Home = item.Location;
        }

        private void OnItemRemoved(Item item, RemovalReason reason)
        {
            if (!_views.TryGetValue(item.Id.Value, out ItemView view))
            {
                return;
            }

            _views.Remove(item.Id.Value);
            _generatorViews.Remove(view);
            view.SetSorting(reason == RemovalReason.Merge ? HeldOrder - 1 : ItemOrder);
            Vector3 to = view.transform.position;
            if (reason == RemovalReason.Merge && _mergeTargetCell >= 0)
            {
                to = new Vector3(Layout.CellCentreX(_mergeTargetCell), Layout.CellCentreY(_mergeTargetCell), 0f);
            }
            else if (reason == RemovalReason.Sell)
            {
                to = new Vector3(Layout.SellX, Layout.SlotY, 0f);
            }

            _tweener.Stop(view.transform);
            _tweener.ShrinkOut(view, to, reason == RemovalReason.Merge ? MergeSeconds : 0.25f);
        }

        private void OnItemMoved(Item item, Location from, Location to)
        {
            if (!_views.TryGetValue(item.Id.Value, out ItemView view))
            {
                return;
            }

            view.Home = to;
            _tweener.Move(view.transform, HomePosition(to), MoveSeconds);
            _tweener.ScaleTo(view.transform, HomeScale(to).x, MoveSeconds);
        }

        private void OnInventoryCapacityChanged(int capacity) => RelayoutIfScreenChanged(true);

        private void OnShrinkFinished(ItemView view) => _pool.Release(view);

        // ------------------------------------------------------------------ test hooks

        /// <summary>Performs a drop exactly as a finger release would (used by PlayMode smoke tests).</summary>
        public InteractionResult SimulateDrop(Location from, PointerTarget target)
        {
            Item item = from.IsBoard ? Engine.GetCell(from.Index) : Engine.GetSlot(from.Index);
            if (item == null || !_views.TryGetValue(item.Id.Value, out ItemView view))
            {
                return InteractionResult.Bounce(ActionError.SourceEmpty, false, -1, null);
            }

            _pressedView = view;
            _pressedAt = from;
            _gesture.Press(0f, 0f);
            _gesture.Move(10000f, 0f);
            BeginDrag();
            _gesture.Release();
            _mergeTargetCell = target.Zone == DropZone.Cell ? target.Cell : -1;
            ItemView held = _held;
            _held = null;
            InteractionResult result = Presenter.Drop(from, held.Id, target);
            _mergeTargetCell = -1;
            held.SetSorting(ItemOrder);
            if (result.Kind == InteractionKind.Bounced)
            {
                _tweener.Move(held.transform, HomePosition(from), BounceSeconds);
                _tweener.ScaleTo(held.transform, HomeScale(from).x, BounceSeconds);
            }
            else if (held.Item != null)
            {
                _tweener.ScaleTo(held.transform, HomeScale(held.Home).x, MoveSeconds);
            }

            AfterCommand();
            return result;
        }

        /// <summary>Performs a tap exactly as a finger tap would (used by PlayMode smoke tests).</summary>
        public InteractionResult SimulateTap(Location at)
        {
            Item item = at.IsBoard ? Engine.GetCell(at.Index) : Engine.GetSlot(at.Index);
            if (item == null || !_views.TryGetValue(item.Id.Value, out ItemView view))
            {
                return default;
            }

            _spawnOriginCell = at.IsBoard ? at.Index : -1;
            InteractionResult result = Presenter.Tap(at, view.Id, NowMs);
            _spawnOriginCell = -1;
            AfterCommand();
            return result;
        }

        /// <summary>Picks up the item at <paramref name="from"/> as if a drag had started (used by tests).</summary>
        public bool SimulateDragStart(Location from)
        {
            Item item = from.IsBoard ? Engine.GetCell(from.Index) : Engine.GetSlot(from.Index);
            if (item == null || !_views.TryGetValue(item.Id.Value, out _pressedView))
            {
                return false;
            }

            _pressedAt = from;
            _gesture.Press(0f, 0f);
            _gesture.Move(10000f, 0f);
            BeginDrag();
            return _held != null;
        }

        /// <summary>One frame of dragging at a screen position: follow, hit-test, hover highlight (used by tests).</summary>
        public void SimulateDragFrame(Vector2 screenPosition)
        {
            if (_held != null)
            {
                UpdateDrag(screenPosition);
            }
        }

        /// <summary>Ends a simulated drag as if the app had been paused: the item returns home, nothing changes.</summary>
        public void SimulateInterruption() => CancelGesture();

        /// <summary>True when every live item has exactly one view at its engine location (used by tests).</summary>
        public bool ViewsMatchEngine()
        {
            if (_views.Count != Engine.ItemCount)
            {
                return false;
            }

            foreach (KeyValuePair<long, ItemView> pair in _views)
            {
                if (!Engine.TryGetItem(new ItemId(pair.Key), out Item item) || pair.Value.Item != item || pair.Value.Home != item.Location)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
