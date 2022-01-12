using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Firefly.Box.Advanced;
using Firefly.Box.Data;
using Firefly.Box.Data.Advanced;
using Firefly.Box.Printing.Advanced;
using Firefly.Box.Testing;
using Firefly.Box.UI.Advanced;
using Firefly.Box.UI.Designer;
using Firefly.Box.UI.GDI;
using WizardOfOz.Witch;
using WizardOfOz.Witch.Engine;
using WizardOfOz.Witch.UI;
using Graphics = Firefly.Box.UI.GDI.Graphics;

namespace Firefly.Box.UI
{
    /// <summary>
    /// Represents a windows ComboBox
    /// </summary>
    [System.ComponentModel.ToolboxItem(true)]
    [ToolboxBitmap(typeof(System.Windows.Forms.ComboBox))]
    [System.ComponentModel.Designer(typeof(ComboBox.ComboBoxDesigner))]
    public class ComboBox : ListControlBase
    {
        class ComboBoxDesigner : Firefly.Box.UI.Designer.ControlDesigner<ComboBox>
        {
            public override void BuildDesigner(IDesignerBuilder<ComboBox> builder)
            {
                builder.AddDataProperty();
                builder.AddProperty("Values", "Values");
                builder.AddProperty("DisplayValues", "DisplayValues");
                builder.AddAction("Next Value", delegate { Instance.NextTab(); });
                builder.AddAction("Previous  Value", delegate { Instance.PreviousTab(); });
            }
        }

        static Command[] _commandsToDisableWhileDropDownIsOpened =
            new[]
                {
                    Command.GoToNextControl, Command.GoToNextRow,
                    Command.GoToPreviousControl, Command.GoToPreviousRow
                };

        public ComboBox()
        {
            _combo.DropDown += delegate { DropDownOpened(); };
            _combo.DropDownClosed += delegate { DropDownClosed(); };

            AddKeyListener(Keys.Control | Keys.Down);
            AddKeyListener(Keys.Control | Keys.Up);
            AddKeyListener(Keys.Alt | Keys.Down);
            ConditionDelegate IsOpen = () => _opened;
            AddKeyListener(Keys.Enter, IsOpen);
            AddKeyListener(Keys.Escape, delegate ()
                                            {
                                                SelectedIndex = _selectedIndexWhenOpened;
                                                _combo.DroppedDown = false;
                                            }, IsOpen);
        }

        internal override void _PerformActionBeforeKeyIsHandled(Keys keys)
        {
            if (_opened && keys == Keys.Tab || keys == (Keys.Shift | Keys.Tab))
                _combo.DroppedDown = false;
        }

        Action _restoreCommandState = () => { };

        void DropDownOpened()
        {
            _opened = true;
            _selectedIndexWhenOpened = SelectedIndex;
            var restore = new List<Action>();
            foreach (var c in _commandsToDisableWhileDropDownIsOpened)
            {
                var cmd = c;
                var old = cmd.Enabled;
                cmd.Enabled = false;
                restore.Add(() => cmd.Enabled = old);
            }
            _restoreCommandState =
                () =>
                {
                    restore.ForEach(action => action());
                    _restoreCommandState = () => { };
                };
        }
        static void Trace(string s)
        {
            return;
            System.Diagnostics.Trace.WriteLine(s);
        }
        [Obsolete("Still in development, do not use for production use, name will change")]
        public bool AutoCompleteAPLHA
        {
            get { return _combo.AutoComplete; }
            set { _combo.AutoComplete = value; }
        }

        void DropDownClosed()
        {
            _restoreCommandState();
            _opened = false;
        }

        bool _opened = false;
        int _selectedIndexWhenOpened = -1;

        [System.ComponentModel.DefaultValue(-1)]
        [WizardOfOz.Witch.UI.Designer.DataCategory]
        [System.ComponentModel.DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get
            {
                return _combo.GetSelectedIndex();
            }
            set
            {
                _combo.SetSelectedIndex(value);
            }
        }
        internal void NextTab()
        {
            UserSelectTab(SelectedIndex + 1);
        }
        internal void PreviousTab()
        {
            UserSelectTab(SelectedIndex - 1);
        }
        void UserSelectTab(int i)
        {
            if (!ReadOnly)
            {
                if (SelectedIndex == i || _combo.GetItemsCount() == 0)
                    return;
                if (i < 0)
                    i = _combo.GetItemsCount() - 1;
                if (i >= _combo.GetItemsCount())
                    i = 0;
                _RunBeforeUserStartsEditing();
                SelectedIndex = i;
            }
        }

        internal override void _ColorsChanged()
        {
            _suspendManager.AddCommandForResume(
                () =>
                {
                    if (_combo.DroppedDown)
                        _combo.DroppedDown = false;
                });
            base._ColorsChanged();
        }


        internal override Color GetBackColorForInnerControls()
        {
            if (DrawMode != DrawMode.Normal || _GetBackColor(true) == Color.Transparent) return base.GetBackColorForInnerControls();
            return _GetBackColor(true);
        }

        internal override Color GetForeColorForInnerControls()
        {
            if (DrawMode != DrawMode.Normal || _GetForeColor(true) == Color.Transparent) return base.GetForeColorForInnerControls();
            return _GetForeColor(true);
        }
        internal override bool _AllowMouseToggleOfActiveGridRowInSelectOnClickGridColumn()
        {
            return false;
        }

        internal override void _ActivateUI()
        {
            base._ActivateUI();

            if (HideSelectionBoxWhileInactiveOnGrid)
            {
                var p = Parent;
                while (p != null && !(p is System.Windows.Forms.Form))
                {
                    if (p is Grid)
                    {
                        _innerComboVisibility = new VisibleOnlyWhenFocused(this);
                        break;
                    }
                    p = p.Parent;
                }
            }

            _innerComboVisibility.Activate();
        }

        InnerComboVisibility _innerComboVisibility = new AlwaysVisible();
        interface InnerComboVisibility
        {
            void Activate();
            void AssertInnerControlVisible(bool visible);
            void Focus();
            void UnFocus();
            void OnLostFocus(Func<bool> hideTheCombo);
            void MouseDownInContainer();
        }

        class AlwaysVisible : InnerComboVisibility
        {
            public void Activate()
            {
            }

            public void AssertInnerControlVisible(bool visible)
            {
                true.ShouldBe(visible);
            }

            public void Focus()
            {
            }

            public void UnFocus()
            {
            }

            public void OnLostFocus(Func<bool> hideTheCombo)
            {
            }

            public void MouseDownInContainer()
            {
            }
        }

        class VisibleOnlyWhenFocused : InnerComboVisibility
        {
            bool _innerControlVisible;
            ComboBox _parent;

            public VisibleOnlyWhenFocused(ComboBox parent)
            {
                _parent = parent;
            }

            public void Activate()
            {
                _innerControlVisible = false;
                _parent._combo.Visible = false;
            }

            public void AssertInnerControlVisible(bool visible)
            {
                _innerControlVisible.ShouldBe(visible);
            }

            public void Focus()
            {
                if (_innerControlVisible) return;
                _innerControlVisible = true;
                _parent._combo.Visible = true;
            }

            public void UnFocus()
            {
                if (!_innerControlVisible) return;
                _innerControlVisible = false;
                _parent._combo.HideIfNotFocused();
            }

            public void OnLostFocus(Func<bool> hideTheCombo)
            {
                if (!_innerControlVisible)
                    hideTheCombo();
            }

            public void MouseDownInContainer()
            {
                if (_parent._IgnoreUserInput() || !_innerControlVisible) return;
                _parent._combo.DroppedDown = true;
            }
        }


        internal override void _Focus()
        {
            _innerComboVisibility.Focus();
            base._Focus();
        }

        internal override void _UnFocus()
        {
            _innerComboVisibility.UnFocus();
            base._UnFocus();
        }

        internal override void InternalVirtualOnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _innerComboVisibility.MouseDownInContainer();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _combo.SetComboHeight(Height);
        }

        internal override Color _GetDefaultBackColor()
        {
            return System.Drawing.SystemColors.Window;
        }

        internal override ControlStyle _GetDefaultControlStyle()
        {
            return ControlStyle.Standard;
        }

        internal override Color _GetDefaultForeColor()
        {
            return System.Drawing.SystemColors.ControlText;
        }
        internal override ListControlInterface _control
        {
            get
            {
                if (_combo != null)
                    return _combo;
                _combo = new WFComboBox(this);
                AddInnerControl(_combo);
                this.Controls.Add(_combo);
                return _combo;
            }
        }
        public virtual DrawMode DrawMode { get { return _combo.DrawMode; } set { _combo.DrawMode = value; } }
        public int PreferredHeight { get { return _combo.PreferredHeight; } }
        internal override bool _ConsumeListKeys()
        {
            return _opened;
        }

        internal override string GetCurrentText()
        {
            return base.GetCurrentText().Trim();
        }

        protected override System.Drawing.Size DefaultSize
        {
            get { return new System.Drawing.Size(120, 21); }
        }
        const int _defaultMaxDropDownLines = 100;
        WFComboBox _combo;
        [Obfuscation(Exclude = true)]
        class WFComboBox : System.Windows.Forms.ComboBox, ListControlInterface
        {
            ComboBox _parent;
            NonTrueTypeFontHelper _fontHelper;

            public WFComboBox(ComboBox parent)
            {
                _parent = parent;
                base.SetStyle(System.Windows.Forms.ControlStyles.SupportsTransparentBackColor, true);
                DropDownStyle = ComboBoxStyle.DropDownList;
                this.DrawMode = DrawMode.OwnerDrawVariable;
                IntegralHeight = false; // important!!! otherwise setting DropDownHeight causes RecreateHandle which we don't want to happen in the OnDropDown handler.

                Dock = DockStyle.Fill;
                this.DropDown += (sender, e) => _parent._RunBeforeUserStartsEditing();
                _fontHelper = new NonTrueTypeFontHelper(this, b => SetStyle(ControlStyles.UserPaint, b));
                MaxDropDownItems = _defaultMaxDropDownLines;

                if (_parent.ForceRightAlignedDropDownButton) RightToLeft = RightToLeft.No;
            }

            public override System.Drawing.Font Font
            {
                get
                {
                    return base.Font;
                }
                set
                {
                    if (base.Font == value) return;
                    _fontHelper.FontPropertySet(value, () => base.Font = value);
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                _fontHelper.OnHandleCreated(() => base.OnHandleCreated(e));
            }

            protected override void OnFontChanged(EventArgs e)
            {
                _fontHelper.OnFontChanged(() => base.OnFontChanged(e));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _fontHelper.Dispose();
                base.Dispose(disposing);
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x00000004;
                    if (DrawMode != DrawMode.Normal) //!System.Windows.Forms.Application.RenderWithVisualStyles && DrawMode != DrawMode.Normal)
                    {
                        cp.Style &= ~0x20;
                        cp.Style |= 0x10;
                    }
                    return cp;
                }
            }

            public override ContextMenuStrip ContextMenuStrip
            {
                get
                {
                    if (DesignMode)
                        return base.ContextMenuStrip;
                    return _parent.ContextMenuStrip ?? base.ContextMenuStrip;
                }
                set
                {
                    base.ContextMenuStrip = value;
                }
            }

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                base.OnDrawItem(e);

                if ((e.State & DrawItemState.Selected).Equals(DrawItemState.Selected) || _parent._GetBackColor((e.State & DrawItemState.ComboBoxEdit) == 0) == Color.Transparent)
                    e.DrawBackground();
                else
                {
                    using (var brush = new SolidBrush(_parent._GetBackColor((e.State & DrawItemState.ComboBoxEdit) == 0)))
                        e.Graphics.FillRectangle(brush, e.Bounds);
                }

                e.DrawFocusRectangle();

                using (var wg = Graphics.FromGraphics(e.Graphics))
                {
                    using (var drawer = new GUIDrawer(wg))
                    {
                        var textBounds = e.Bounds;
                        textBounds.Inflate((e.State & DrawItemState.ComboBoxEdit).Equals(DrawItemState.ComboBoxEdit) ? -1 : -2, 0);
                        if (_parent.DesignMode)
                        {
                            var data = string.Empty;
                            if (_parent.Data != null)
                                data = _parent.Data.ToString().Trim();
                            if (e.Index >= 0)
                                data = string.Format("{0} (Context {1})", data, e.Index);
                            drawer.DrawString(data, e.Font, ForeColor, textBounds, _parent._GetStandardStringDrawingProperties(false));
                        }
                        else if (e.Index >= 0)
                        {
                            Color foreColor;
                            if ((e.State & DrawItemState.Selected).Equals(DrawItemState.Selected))
                                foreColor = System.Drawing.SystemColors.HighlightText;
                            else
                                foreColor = _parent._GetForeColor((e.State & DrawItemState.ComboBoxEdit) == 0);

                            drawer.DrawString(GetItem(e.Index), e.Font, foreColor, textBounds, _parent._GetStandardStringDrawingProperties((e.State & DrawItemState.ComboBoxEdit) == 0));
                        }
                    }
                }
            }

            protected override bool ProcessKeyEventArgs(ref Message m)
            {
                if (_readOnly) return true;
                return base.ProcessKeyEventArgs(ref m);
            }

            bool _readOnly = false;
            protected override void WndProc(ref Message m)
            {
                if (_readOnly &&
                    (m.Msg == 0x2111 || m.Msg == 0x0203)) // WM_REFLECT + WM_COMMAND
                    return;
                if (m.Msg == 0x2111 && ((int)m.WParam >> 16 & (int)ushort.MaxValue) == 10)
                    ResetSelectionList();
                base.WndProc(ref m);
            }

            int[] ListControlInterface.GetSelectedInices()
            {
                var r = new int[] { -1 };
                _parent._InvokeUIPlatformCommand(
                    () =>
                    {
                        r = new[] { GetSelectedIndex() };
                    });
                return r;
            }

            int _lastSelectedIndex = -1;
            void ListControlInterface.SetSelectedIndices(int[] values)
            {
                if (IsDisposed) return;

                BeginUpdate();
                try
                {
                    if (values[0] >= 0)
                        _lastSelectedIndex = values[0];
                    SetSelectedIndex(values[0]);
                }
                finally
                {
                    _parent._suspendManager.AddCommandForAfterResume(
                        () =>
                        {
                            if (IsDisposed) return;
                            EndUpdate();
                        });
                }
            }

            protected override void OnSelectedIndexChanged(EventArgs e)
            {
                if (AutoComplete)
                {
                    var prev = _autoCompleteSelectedIndex;
                    if (SelectedIndex == -1)
                        _autoCompleteSelectedIndex = -1;
                    else
                        _autoCompleteSelectedIndex = _items.IndexOf(GetItem(SelectedIndex));
                    if (prev != _autoCompleteSelectedIndex && _selectedIndexChanged != null)
                        _selectedIndexChanged();
                }
                else if (_selectedIndexChanged != null)
                    _selectedIndexChanged();
            }

            event Action _selectedIndexChanged;

            void ListControlInterface.RegisterSelectedIndexObserver(Action observer)
            {
                _selectedIndexChanged += observer;
            }
            void ListControlInterface.Focus()
            {
                Select();
            }

            void ListControlInterface.SetReadOnly(bool value)
            {
                if (value && IsHandleCreated)
                    DroppedDown = false;
                _readOnly = value;
            }

            bool _autoCompete;
            public bool AutoComplete
            {
                get { return _autoCompete; }
                set
                {
                    _autoCompete = value;
                    if (value && !_parent.DesignMode)
                    {
                        DropDownStyle = ComboBoxStyle.DropDown;
                        _items.Clear();
                        _items.AddRange(GetItems());
                        _autoCompleteSelectedIndex = SelectedIndex;
                    }
                    else
                        DropDownStyle = ComboBoxStyle.DropDownList;
                }
            }
            public void ResetOptions(string[] options)
            {
                if (IsDisposed) return;
                BeginUpdate();
                try
                {
                    if (AutoComplete)
                    {
                        _items.Clear();
                        _items.AddRange(options);
                    }
                    else
                    {
                        Items.Clear();
                        AddItems(options);
                    }
                }
                finally
                {
                    EndUpdate();
                }
            }
            internal void SetSelectedIndex(int value)
            {

                if (AutoComplete)
                {
                    if (DroppedDown)
                        return;
                    Trace("Set selected index " + value);
                    if (_autoCompleteSelectedIndex != value)
                    {
                        _autoCompleteSelectedIndex = value;
                        if (_selectedIndexChanged != null)
                            _selectedIndexChanged();
                        if (value < 0)
                            Text = "";
                        else
                            Text = _items[_autoCompleteSelectedIndex];
                    }
                }
                else
                    SelectedIndex = value;
            }
            internal int GetSelectedIndex()
            {
                if (AutoComplete)
                    return _autoCompleteSelectedIndex;
                return SelectedIndex;
            }
            public int GetItemsCount()
            {
                if (AutoComplete)
                    return _items.Count;
                return Items.Count;
            }
            string _lastText = "";
            bool _suppressChange = false;
            void RefreshListSelectionAccordingToText()
            {
                if (_suppressChange)
                    return;
                Trace("refresh list " + Text);
                _suppressChange = true;
                int start = SelectionStart;
                var s = Text;

                if (start < s.Length)
                    s = s.Remove(start);
                if (string.Equals(s, _lastText, StringComparison.InvariantCultureIgnoreCase) && s.Length > 0)
                {
                    s = s.Remove(s.Length - 1);
                    start--;
                }

                _lastText = s;

                _autoCompleteSelectedIndex = -1;
                int i = 0;
                var result =
                    _items.FindAll(
                    x =>
                    {

                        if (x.ToString().StartsWith(s, StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (_autoCompleteSelectedIndex == -1 || string.Equals(x.ToString(), s, StringComparison.InvariantCultureIgnoreCase))
                                _autoCompleteSelectedIndex = i;
                            i++;
                            return true;
                        }
                        i++;
                        return false;
                    }
                        )
                    .ToArray();
                if (result.Length > 0)
                {
                    var ss = result[0];
                    Text = start > 0 ? ss : "";
                }
                if (!Enumerable.SequenceEqual(GetItems(), result))
                {
                    BeginUpdate();
                    Items.Clear();
                    AddItems(result);
                    EndUpdate();
                    DropDownHeight = (GetItemHeight()) * Math.Min(Math.Max(Items.Count, 1), MaxDropDownItems) + 2;
                    typeof(System.Windows.Forms.ComboBox).GetMethod("UpdateDropDownHeight", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, new object[0]);
                }
                if (!DroppedDown)
                {
                    DroppedDown = true;
                    Cursor.Current = this.Cursor;
                }
                if (start > 0)
                    Select(start, Text.Length - start);
                if (_selectedIndexChanged != null)
                    _selectedIndexChanged();
                _suppressChange = false;
            }
            readonly List<string> _items = new List<string>();
            int _autoCompleteSelectedIndex = -1;
            protected override void OnTextUpdate(EventArgs e)
            {
                base.OnTextUpdate(e);
                if (AutoComplete)
                    RefreshListSelectionAccordingToText();
            }
            bool ListControlInterface.IsInputKey(Keys keys)
            {
                return IsInputKey(keys);
            }

            Action _onCreate = delegate { };
            protected override void OnCreateControl()
            {
                _onCreate();
                base.OnCreateControl();
            }
            public void SetComboHeight(int value)
            {
                _onCreate = delegate
                                {
                                    ItemHeight = Math.Max(value - 6, 1);
                                    if (Height < value)
                                        _parent.Height = Height;
                                    _onCreate = delegate { };
                                };
                if (Created)
                    _onCreate();
            }

            protected override void OnDropDown(EventArgs e)
            {
                ResetSelectionList();
                base.OnDropDown(e);
                var screenBounds = Screen.FromControl(this).Bounds;
                var maxComboHeight = GetItemHeight() * Math.Min(Math.Max(Items.Count, 1), MaxDropDownItems) + 2;
                var heightFromBottomOfComboToBottomOfScreen = screenBounds.Bottom - PointToScreen(new Point(0, Bottom)).Y;
                if (!_parent.ForceBoxVisibleWhileDroppedDown || heightFromBottomOfComboToBottomOfScreen >= maxComboHeight)
                    DropDownHeight = Math.Min(screenBounds.Height, maxComboHeight);
                else
                    DropDownHeight = Math.Max(heightFromBottomOfComboToBottomOfScreen, PointToScreen(new Point(0, 0)).Y - screenBounds.Top);
            }

            void ResetSelectionList()
            {
                if (AutoComplete && !_suppressChange)
                {
                    BeginUpdate();
                    Items.Clear();
                    AddItems(_items.ToArray());
                    if (_autoCompleteSelectedIndex == -1 && _lastSelectedIndex < _items.Count)
                    {
                        SelectedIndex = _lastSelectedIndex;
                    }
                    else
                    {
                        SelectedIndex = _autoCompleteSelectedIndex;
                    }


                    EndUpdate();
                }
            }

            protected override void OnMeasureItem(MeasureItemEventArgs e)
            {
                base.OnMeasureItem(e);
                e.ItemHeight = GetItemHeight();
            }

            int GetItemHeight()
            {
                return (int)Firefly.Box.UI.GDI.Font.GetFontWithCaching(Font).AverageCharSize.Height;
            }

            public void InvokeMouseWheel(HandledMouseEventArgs args)
            {
                OnMouseWheel(args);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (_parent._opened)
                    base.OnMouseWheel(e);
                else
                    _parent._actionsForControls.OnMouseWheel(e);
            }

            public void UserSetTextTo(string s, int selectionStart, int selectionLength)
            {
                Text = s;
                Select(selectionStart, selectionLength);
                this.RefreshListSelectionAccordingToText();
            }

            public string GetTextProperty(string s)
            {
                if (AutoComplete && DroppedDown)
                    return Text;
                return s;
            }

            void AddItems(string[] itemsToAdd)
            {
                var itemsToAdd1 = new string[itemsToAdd.Length];
                for (var i = 0; i < itemsToAdd.Length; i++)
                {
                    var s = itemsToAdd[i];
                    if (DrawMode == DrawMode.Normal)
                    {
                        for (int j = 0; j < s.Length; j++)
                        {
                            if (s[j] == '&')
                                s = s.Remove(j, 1);
                        }
                    }
                    else
                    {
                        var x = FindMnemonic(s);
                        if (x != (char)0)
                            s = x + s;
                    }
                    itemsToAdd1[i] = string.IsNullOrEmpty(s) ? " " : s;
                }
                Items.AddRange(itemsToAdd1);
            }

            char FindMnemonic(string text)
            {
                var x = text.IndexOf('&');
                return x != -1 && x + 1 < text.Length && text[x + 1] != ' ' ? text[x + 1] : (char)0;
            }

            string GetItem(int index)
            {
                var x = (string)Items[index];
                return FindMnemonic(x) != (char)0 ? x.Substring(1) : x;
            }

            public string[] GetItems()
            {
                var result = new string[Items.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = GetItem(i);
                return result;
            }

            public void HideIfNotFocused()
            {
                if (!Focused) Visible = false;
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                _parent._innerComboVisibility.OnLostFocus(() => Visible = false);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (!DroppedDown && (e.KeyData == Keys.Down || e.KeyData == Keys.Up || e.KeyData == (Keys.Control | Keys.Down) || e.KeyData == (Keys.Control | Keys.Up) || e.KeyData == (Keys.PageDown) || e.KeyData == (Keys.PageUp)))
                    e.Handled = true;
                base.OnKeyDown(e);
            }

            public override RightToLeft RightToLeft
            {
                get
                {
                    return _parent != null && _parent.ForceRightAlignedDropDownButton ? RightToLeft.No : base.RightToLeft;
                }

                set
                {
                    if (_parent.ForceRightAlignedDropDownButton) base.RightToLeft = RightToLeft.No;
                    base.RightToLeft = value;
                }
            }
        }

        [Browsable(false)]
        public bool ForceRightAlignedDropDownButton { get; set; }

        /// <summary>
        /// The number of lines that will appear when the <This/> is opened
        /// </summary>
        [WizardOfOz.Witch.UI.Designer.DataCategory]
        public int Lines
        {
            get
            {
                return _combo.MaxDropDownItems;
            }
            set
            {
                _combo.MaxDropDownItems = value > 0 && value <= 100 ? value : _defaultMaxDropDownLines;
            }
        }


        internal void AssertList(params string[] values)
        {
            Should.Equal(values, _combo.GetItems());
        }

        internal void AssertForeColor(Color color)
        {
            Should.Equal(color, _combo.ForeColor);
        }

        internal void AssertBackColor(Color color)
        {
            Should.Equal(color, _combo.BackColor);
        }
        internal override ControlStyleInterface CreateStyleFrom(ControlStyle style)
        {
            ControlStyleInterface result = base.CreateStyleFrom(style);
            switch (style)
            {
                case ControlStyle.Raised:
                case ControlStyle.Sunken:
                    return new ListControlSunkenStyle(result);
                case ControlStyle.Standard:
                    return new ListControlStandartStyle(result);
                default:
                    return result;
            }
        }

        public event BindingEventHandler<IntBindingEventArgs> BindLines
        {
            add { _binding.Add("Lines", () => Lines, x => Lines = x, value); }
            remove { _binding.Remove("Lines", value); }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _combo.Dispose();
        }

        internal override ControlLayer _GetLayer()
        {
            return NormalControlLayer.Instance;
        }

        internal void DoWhileOpenForTesting(Action doThis)
        {
            DropDownOpened();
            try
            {
                doThis();
            }
            finally
            {
                DropDownClosed();
            }
        }

        public override string Text
        {
            get { return _combo.GetTextProperty(base.Text); }
            set
            {
                base.Text = value;
            }
        }

        internal object InnerTextTEST
        {
            get { return _combo.Text; }
        }

        public bool HideSelectionBoxWhileInactiveOnGrid { get; set; }

        internal void InvokeMouseWheelOnInnerControl(HandledMouseEventArgs args)
        {
            _combo.InvokeMouseWheel(args);
        }

        internal Control GetInnerComboForTesting()
        {
            return _combo;
        }
        internal void UserSetTextToFORTESTING(string s, int selectionStart, int selectionLength)
        {
            _combo.UserSetTextTo(s, selectionStart, selectionLength);
            Context.Current.UICommands.Flush(false);
        }

        internal void InternalSelectedIndexChangedFORTESTING(int i)
        {
            _combo.DroppedDown = true;
            _combo.DroppedDown = false;
            try
            {
                _combo.SelectedIndex = i;
            }
            finally
            {
                Context.Current.UICommands.Flush(false);
            }
        }

        public void CloseDropDownforTESTING()
        {
            _combo.DroppedDown = false;
        }

        internal void AssertInnerControlVisible(bool visible)
        {
            _innerComboVisibility.AssertInnerControlVisible(visible);
        }

        [Browsable(false)]
        public bool ForceBoxVisibleWhileDroppedDown { get; set; }

    }

}