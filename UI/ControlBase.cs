using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Firefly.Box;
using Firefly.Box.Advanced;
using Firefly.Box.Data.Advanced;
using Firefly.Box.Printing;
using Firefly.Box.Testing;
using Firefly.Box.UI;
using Firefly.Box.UI.GDI;
using WizardOfOz.Witch.Engine;
using WizardOfOz.Witch.Printing;
using WizardOfOz.Witch.Types;
using WizardOfOz.Witch.UI;
using WizardOfOz.Witch.UI.Designer;
using Control = System.Windows.Forms.Control;
using Cursor = System.Windows.Forms.Cursor;
using Font = System.Drawing.Font;
using Graphics = Firefly.Box.UI.GDI.Graphics;


namespace Firefly.Box.UI.Advanced
{

    [Doc.BindDocumentation]
    [System.ComponentModel.ToolboxItem(false)]
    [TypeDescriptionProvider(typeof(ValueInheritanceTypeDescriptionProvider))]
    public class ControlBase : System.Windows.Forms.Control, WizardOfOz.Witch.Printing.PrintableControl, WizardOfOz.Witch.UI.IControl,
                               TransparencySupportingControl, PropertyDecorator, AttachedControl, ParentOfTransparentControls,
                               CanPaintChildControls, CanBePaintedByParent, HasDisplaySize
    {
        internal Binding _binding;
        public ControlBase()
        {
            _tabIndexBinding = new Binding(this);
            _controlExtender = new ControlExtender(new myControlExtenderClient(this), this);
            _binding = new Binding(this);
            _dragDropBinding = new Binding(this);
            _childControlPainter = new ChildControlPaintingHelper(new myChildControlPaintingHelperClient(this));
            _childControlPainter.PaintChildControls = _AlwaysPaintChildControls();
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.Opaque, true);
            SetStyle(ControlStyles.ResizeRedraw, true);

            SetStyle(ControlStyles.Selectable, _IsSelectable());
            SetStyle(ControlStyles.CacheText, true);

            bool entered = false;
            _getDesignMode = delegate { return base.DesignMode; };


            _attachableControl = new myAttachableControl(this);
            _backgroundPainter = new TransparentPainter(this);
            _toolTipProvider = new ToolTip(_components);
            //_toolTipProvider.Popup += (s, a) => a.ToolTipSize += new Size(50,0);

            _backColor = new Property<System.Drawing.Color>(
                delegate (System.Drawing.Color value)
                {
                    if (!_IsPrinting())
                        base.BackColor = value;
                    _ColorsChanged();
                    DoInvalidate();
                }, _GetDefaultBackColor);
            _foreColor = new Property<System.Drawing.Color>(
                delegate (System.Drawing.Color value)
                {
                    if (!_IsPrinting())
                        base.ForeColor = value;
                    _ColorsChanged();
                    DoInvalidate();
                }, _GetDefaultForeColor);

            _setInnerControlsColors = new SuspendedCommandClass(() => _suspendManager,
                () =>
                {
                    DoOnInnerControls(
                        delegate (WinformsControl wc)
                        {
                            if (wc.ColorsAsOuterControl)
                                wc.Apply(
                                    control =>
                                    {
                                        control.BackColor = GetBackColorForInnerControls();
                                        control.ForeColor = GetForeColorForInnerControls();
                                    });
                        });
                });

            _style = new Property<ControlStyle>(delegate (ControlStyle value)
                                                    {
                                                        _ColorsChanged();
                                                        _StyleChanged();
                                                        Refresh();
                                                        DoInvalidate();
                                                    }, () => _GetDefaultControlStyle(), true, false, v => SetStyle(v));
            SetStyle(_GetDefaultControlStyle());
            _getBorderColor = DefaultGetBorderColor;
            _borderColor = new Property<Color>(delegate (Color value)
            {
                _ColorsChanged();
                DoInvalidate();
            }, _getBorderColor());
            _rightToLeft = new Property<System.Windows.Forms.RightToLeft>(delegate (System.Windows.Forms.RightToLeft value)
                                                                              {
                                                                                  DoOnInnerControls(control => control.RightToLeft = value);
                                                                                  _toolTipProvider.RightToLeft = GetRightToLeft();
                                                                                  _RightToLeftChanged();
                                                                                  DoInvalidate();
                                                                              }, System.Windows.Forms.RightToLeft.Inherit);
            _rightToLeftLayout = new Property<bool>(delegate (bool value)
                                                        {
                                                            _RightToLeftChanged();
                                                            DoInvalidate();
                                                        }, true);



            _visible = new Property<bool>(value =>
                                              {
                                                  _suspendManager.Layout.SetVisible(this, value);
                                              }, () => true, true, false, _OnVisibleChanged);
            _enabled = new Property<bool>(
                delegate (bool value)
                {
                    if (!_IsPrinting())
                        base.Enabled = value;
                    DoOnInnerControls(delegate (WinformsControl control)
                    {

                        control.Apply(c =>
                        {
                            c.Enabled = value;
                            if (control.ColorsAsOuterControl)
                                c.BackColor = GetBackColorForInnerControls();
                        });


                    });
                    DoInvalidate();
                    if (!value)
                        _actionsForControls.ControlDisabled(this);
                }, () => true, true, false, x => _attachableControl.EnabledChanged(x));
            _toolTip = new Property<string>(
                delegate (string value)
                {
                    var newValue = Translate(value.TrimEnd(' '));
                    System.EventHandler setToolTip = new System.EventHandler(delegate { });
                    setToolTip = new System.EventHandler(
                        delegate
                        {
                            Application.Idle -= setToolTip;
                            if (IsDisposed) return;
                            _toolTipProvider.SetToolTip(this, newValue);
                            DoOnInnerControls(
                                delegate (System.Windows.Forms.Control control)
                                {
                                    _toolTipProvider.SetToolTip(control, newValue);
                                });
                        });
                    Application.Idle += setToolTip;
                }, "");

            base.RightToLeft = _rightToLeft.Value;

            _colorScheme = UI.ColorScheme.Create(_foreColor, _backColor, "ForeColor", "BackColor");

            Action<bool> deferredBoundsChanged =
                moveOnly =>
                {
                    _controlExtender.BoundsChanged(moveOnly);
                    _suspendManager.AddSuspendedCommandWithId(_deferredBoundsChangeCommandID,
                        delegate
                        {
                            var left = _virtualParent.ToReal(_deferredLeft.GetValue());
                            var bs = BoundsSpecified.None;
                            foreach (var d in new[] { _deferredLeft, _deferredTop, _deferredWidth, _deferredHeight })
                            {
                                d.AddBoundsSpecified(specified => bs |= specified);
                            }
                            _controlExtender.Moved();
                            _suspendManager.Layout.SetBounds(this,
                                left, _deferredTop.GetValue(),
                                _deferredWidth.GetValue(), _deferredHeight.GetValue(),
                                bs,
                                delegate
                                {
                                    base.SetBoundsCore(
                                        left, _deferredTop.GetValue(),
                                        _deferredWidth.GetValue(), _deferredHeight.GetValue(),
                                        bs);
                                });
                        });
                };
            _deferredLeft = new BoundsSetter(() => _virtualParent.ToVirtual(Left), BoundsSpecified.X, () => deferredBoundsChanged(true));
            _deferredTop = new BoundsSetter(() => Top, BoundsSpecified.Y, () => deferredBoundsChanged(true));
            _deferredWidth = new BoundsSetter(() => Width, BoundsSpecified.Width, () => deferredBoundsChanged(false));
            _deferredHeight = new BoundsSetter(() => Height, BoundsSpecified.Height, () => deferredBoundsChanged(false));
        }

        internal ControlBase _GetPseudoContainer()
        {
            return _controlBindingControl.GetPseudoContainer();
        }

        internal DataDisplayer WrapDataDisplayer(DataDisplayer dsp)
        {
            return new UIThreadWrapDataDisplayer(dsp, this);
        }

        class UIThreadWrapDataDisplayer : DataDisplayer
        {
            DataDisplayer _wrapped;
            ControlBase _parent;

            public UIThreadWrapDataDisplayer(DataDisplayer wrapped, ControlBase parent)
            {
                _wrapped = wrapped;
                _parent = parent;
            }

            public void SetRight2Left(bool value)
            {
                _parent._actionsForControls.DoOnUIThread(() =>
                                                         _wrapped.SetRight2Left(value));
            }

            public void SetDataText(string data, bool autoSkip)
            {
                _parent._actionsForControls.DoOnUIThread(() =>
                   _wrapped.SetDataText(data, autoSkip));
            }

            public void SetNumberText(string data, bool autoSkip, bool emptyValue)
            {
                _parent._actionsForControls.DoOnUIThread(() =>
                   _wrapped.SetNumberText(data, autoSkip, emptyValue));
            }

            public void SetMaskProvider(MaskProvider provider)
            {
                _parent._actionsForControls.DoOnUIThread(() =>
                   _wrapped.SetMaskProvider(provider));
            }

            public void SetImeMode(ImeMode imeMode)
            {
                _parent._actionsForControls.DoOnNonUIThread(() => _wrapped.SetImeMode(imeMode));
            }
        }


        #region binding methods
        protected void AddBindingEvent(string name, Func<bool> getValue, Action<bool> setValue, BindingEventHandler<BooleanBindingEventArgs> handler)
        {
            _binding.Add(name, getValue, setValue, handler);
        }

        protected void AddBindingEvent(string name, Func<string> getValue, Action<string> setValue, BindingEventHandler<StringBindingEventArgs> handler)
        {
            _binding.Add(name, getValue, setValue, handler, false);
        }

        protected void AddBindingEvent(string name, Func<int> getValue, Action<int> setValue, BindingEventHandler<IntBindingEventArgs> handler)
        {
            _binding.Add(name, getValue, setValue, handler);
        }

        protected void AddBindingEvent(string name, Func<Color> getValue, Action<Color> setValue, BindingEventHandler<ColorBindingEventArgs> handler)
        {
            _binding.Add(name, getValue, setValue, handler);
        }

        protected void AddBindingEvent(string name, Func<Font> getValue, Action<Font> setValue, BindingEventHandler<FontBindingEventArgs> handler)
        {
            _binding.Add(name, getValue, setValue, handler);
        }

        protected void AddBindingEvent(string name, Func<ContextMenuStrip> getValue, Action<ContextMenuStrip> setValue, BindingEventHandler<ContextMenuStripBindingEventArgs> handler)
        {
            _binding.Add(name, getValue, setValue, handler);
        }

        protected void RemoveBindingEvent(string name, BindingEventHandler<BooleanBindingEventArgs> handler)
        {
            _binding.Remove(name, handler);
        }

        protected void RemoveBindingEvent(string name, BindingEventHandler<StringBindingEventArgs> handler)
        {
            _binding.Remove(name, handler);
        }

        protected void RemoveBindingEvent(string name, BindingEventHandler<IntBindingEventArgs> handler)
        {
            _binding.Remove(name, handler);
        }

        protected void RemoveBindingEvent(string name, BindingEventHandler<ColorBindingEventArgs> handler)
        {
            _binding.Remove(name, handler);
        }

        protected void RemoveBindingEvent(string name, BindingEventHandler<FontBindingEventArgs> handler)
        {
            _binding.Remove(name, handler);
        }

        protected void RemoveBindingEvent(string name, BindingEventHandler<ContextMenuStripBindingEventArgs> handler)
        {
            _binding.Remove(name, handler);
        }
        #endregion
        class myControlForAdvancedAnchor : ControlForSpecialAnchorManager
        {
            ControlBase _parent;

            public myControlForAdvancedAnchor(ControlBase parent)
            {
                _parent = parent;
            }

            public Control Parent
            {
                get { return _parent.Parent; }
            }

            public void SetBounds(int left, int top, int width, int height, int clippedWidth)
            {
                _parent._clippedWidth = clippedWidth;
                if (left == _parent.DeferredLeft && top == _parent.DeferredTop && width == _parent.DeferredWidth && height == _parent.DeferredHeight)
                    return;
                _parent._BoundsModifiedDueToAdvancedAnchor();
                _parent.SetBounds(_parent._virtualParent.ToReal(left), top, width, height);
            }

            public void SetAnchor(AnchorStyles anchorStyles)
            {
                _parent.Anchor = anchorStyles;
            }

            public Rectangle GetCurrentBounds()
            {
                return new Rectangle(_parent._deferredLeft.GetValue(), _parent._deferredTop.GetValue(),
                                     _parent._deferredWidth.GetValue() + _parent._clippedWidth, _parent._deferredHeight.GetValue());
            }
        }

        internal virtual void _BoundsModifiedDueToAdvancedAnchor()
        {

        }

        internal virtual void _DoDragDrop(Action<IDataObject> doOnDataObject)
        {
            if (AllowDrag)
            {
                DragDropData = new DataObject();
                try
                {
                    doOnDataObject(DragDropData);
                    _InvokeActionAndReturnTrueIfHandled(Command.DragStart);
                    DoDragDrop(DragDropData, DragDropEffects.All);
                }
                finally
                {
                    DragDropData = null;
                }
            }
        }

        internal VirtualControlParent _virtualParent = new VirtualControlParentDummy();
        internal void _SetVirtualParent(VirtualControlParent virtualParent)
        {
            _virtualParent = virtualParent;
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (Parent != null)
                Parent.PerformLayout(this, "Location");
        }

        internal virtual bool _IsSelectable()
        {
            return false;
        }

        public static string[] DragDropFormats;
        public static IDataObject DragDropData;
        public static DragEventArgs DragDropEventArgs;
        public static System.Windows.Forms.Control DragDropControl;

        internal virtual int _GetDeltaToUndoWhenRecalculatingWidthAndReset()
        {
            return 0;
        }

        protected virtual string Translate(string text)
        {
            return text;
        }

        Guid _deferredBoundsChangeCommandID = Guid.NewGuid();

        class BoundsSetter
        {
            Func<int> _getCurrentValue;
            BoundsSpecified _bs;
            Func<int> _getValue;
            Action _newValueWasSet;
            public BoundsSetter(Func<int> getCurrentValue, BoundsSpecified bs, Action newValueWasSet)
            {
                _getCurrentValue = getCurrentValue;
                _bs = bs;
                _newValueWasSet = newValueWasSet;
                _getValue = _getCurrentValue;
            }

            public int GetValue()
            {
                return _getValue();
            }

            public void Reset()
            {
                _getValue = _getCurrentValue;
            }

            public void SetNewValue(int value)
            {
                _getValue = () => value;
                _newValueWasSet();
            }

            public void Committed()
            {
                _getValue = () => _getCurrentValue();
            }

            public void AddBoundsSpecified(Action<BoundsSpecified> add)
            {
                if (_getValue() != _getCurrentValue())
                    add(_bs);
                else
                    Committed();
            }
        }

        internal virtual void _OnVisibleChanged(bool visible)
        {
            _NotifyVisibleChangedToAttachableControl(visible);
            CheckBeforeInvalidating(ClientRectangle, delegate { }, !visible);
        }

        internal virtual void _NotifyVisibleChangedToAttachableControl(bool visible)
        {
            _attachableControl.VisibleChanged(visible);
        }

        internal virtual void BackColorChangedTo(Color color)
        {

        }

        internal void _EditingEndedWithoutLeave()
        {
            _actionsForControls.ControlEditingEndedWithoutLeave();
        }

        internal virtual bool _AlwaysPaintChildControls()
        {
            return false;
        }
        protected new bool DesignMode
        {
            get { return _getDesignMode(); }
        }

        internal void DoLeftMouseDownMouseUpForTesting(int x, int y)
        {
            InternalOnMouseDown(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
            InternalVirtualOnMouseUp(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
        }

        internal void DoRightMouseDownMouseUpForTesting(int x, int y)
        {
            InternalOnMouseDown(new MouseEventArgs(MouseButtons.Right, 0, x, y, 0));
            InternalVirtualOnMouseUp(new MouseEventArgs(MouseButtons.Right, 0, x, y, 0));
        }

        internal void DoLeftMouseDownMouseUpForTesting(int x, int y, Action<Action> wrapMouseDown)
        {
            wrapMouseDown(() => InternalOnMouseDown(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0)));
            InternalVirtualOnMouseUp(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
        }

        class myChildControlPaintingHelperClient : ChildControlPaintingHelperClient
        {
            ControlBase _parent;

            public myChildControlPaintingHelperClient(ControlBase parent)
            {
                _parent = parent;
            }

            public void RecreateHandle()
            {
                _parent.RecreateHandle();
            }

            public void Invalidate(Rectangle invalidRect)
            {
                if (_parent.Invalidated != null) _parent.Invalidated(invalidRect);
                _parent.Invalidate(invalidRect, false);
            }

            public void RegisterCreateHandleListener(CreateHandleListener listener)
            {
                _parent._createHandleListener = listener;
            }

            public void RegisterCreateParamsModifier(Action<CreateParams> modifier)
            {
                _parent._createParamsModifier = modifier;
            }

            public void RegisterInvalidateByChildListener(Action<Rectangle> listener)
            {
                _parent._invalidateByChildListener = listener;
            }
        }

        ChildControlPaintingHelper _childControlPainter;

        Action<CreateParams> _createParamsModifier = delegate { };
        protected override CreateParams CreateParams
        {
            get
            {
                System.Windows.Forms.CreateParams cp = base.CreateParams;
                _createParamsModifier(cp);
                cp.ExStyle |= 0x00000004;
                return cp;
            }
        }

        internal event Action Updated;
        internal void DoUpdate()
        {
            var f = FindForm() as Form;
            if (f != null && f.Closing) return;
            if (IsPaintedByParent())
            {
                var parent = (CanPaintChildControls)this.Parent;
                parent.UpdateByChild();
            }
            else
                Update();
            if (Updated != null)
                Updated();
        }

        bool CanPaintChildControls.PaintsChildControls()
        {
            return _childControlPainter.PaintsChildControls();
        }

        Action<Rectangle> _invalidateByChildListener = delegate { };
        void CanPaintChildControls.InvalidateByChild(Rectangle invalidRect)
        {
            CheckBeforeInvalidating(invalidRect, delegate () { _invalidateByChildListener(invalidRect); }, false);
        }

        void CanPaintChildControls.UpdateByChild()
        {
            DoUpdate();
        }

        void CheckBeforeInvalidating(Rectangle invalidRect, Action invalidate, bool forceInvalidateParent)
        {
            _DoUnlessInGridPaintingState(
                () =>
                {
                    if (IsPaintedByParent() || forceInvalidateParent)
                    {
                        var parent = this.Parent as CanPaintChildControls;
                        if (parent != null)
                        {
                            Rectangle newInvalidRect = invalidRect;
                            newInvalidRect.Offset(Location.X, Location.Y);
                            parent.InvalidateByChild(newInvalidRect);
                        }
                    }
                    else
                        invalidate();
                    ControlHasBeenInvalidated();
                });
        }

        internal void DoInvalidate(Rectangle invalidRect)
        {
            if (!_IsPrinting())
                CheckBeforeInvalidating(invalidRect, delegate ()
                {
                    Invalidate(invalidRect);
                    if (Invalidated != null) Invalidated(invalidRect);
                }, false);
        }

        internal virtual void ControlHasBeenInvalidated()
        {
        }

        System.ComponentModel.IContainer _components = new System.ComponentModel.Container();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_components != null)
                {
                    _components.Dispose();
                    _controlExtender.DoDispose();
                }
                ContextMenuStrip = null;
                if (_IsPrinting() && this.WindowTarget != null)
                    System.GC.SuppressFinalize(this.WindowTarget);
            }
            base.Dispose(disposing);
        }

        internal virtual void _RightToLeftChanged()
        {
        }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
        }

        internal new event Action<Rectangle> Invalidated;
        internal bool ForTestingForceVisible;

        internal bool _creatingControl = false;
        internal void CreateTheControl()
        {
            _creatingControl = true;
            try
            {
                if (!IsHandleCreated)
                    CreateHandle();
                System.Windows.Forms.Control[] controlArray1 = new System.Windows.Forms.Control[this.Controls.Count];
                this.Controls.CopyTo(controlArray1, 0);
                foreach (System.Windows.Forms.Control control1 in controlArray1)
                {
                    ControlBase controlBase = control1 as ControlBase;
                    if (controlBase != null)
                        controlBase.CreateTheControl();
                }
            }
            finally
            {
                _creatingControl = false;
            }
        }

        System.Collections.Generic.List<WinformsControl> _innerControls = new List<WinformsControl>();


        internal void AddInnerControl(System.Windows.Forms.Control control)
        {
            _innerControls.Add(new WinformsControlAdapter(control));
        }
        internal void AddInnerControl(WinformsControl control)
        {
            _innerControls.Add(control);
        }

        internal void DoOnInnerControls(WinformsControlAction action)
        {
            foreach (WinformsControl control in _innerControls)
            {
                control.Apply(action);
            }
        }

        internal void DoOnInnerControls(System.Action<WinformsControl> action)
        {
            _innerControls.ForEach(action);
        }

        ToolTip _toolTipProvider;


        System.Collections.Generic.List<PropertyWrapper> _properties;
        internal void CollectProperties()
        {
            if (_properties == null)
            {
                _properties = new List<PropertyWrapper>();
                AddProperties(new PropertiesCollectorClass(this));
            }
        }
        internal interface PropertiesCollector
        {
            void AddProperties(params UntypedProperty[] props);
            void AddNonSuspendableProperties(params UntypedProperty[] props);
        }
        class PropertyWrapper
        {
            public UntypedProperty Property;
            public bool Suspendable;

            public PropertyWrapper(UntypedProperty property, bool suspendable)
            {
                Property = property;
                Suspendable = suspendable;
            }

            public void SetSuspendManager(Func<Suspended> manager, SuspendedCommandStillValid commandStillValid)
            {
                if (Suspendable)
                    Property.SetSuspendManager(manager, commandStillValid);
            }



            public void SimpleMode()
            {
                Property.SwitchToSimpleMode();
            }
        }
        class PropertiesCollectorClass : PropertiesCollector
        {
            ControlBase _parent;
            public PropertiesCollectorClass(ControlBase parent)
            {
                _parent = parent;
            }

            public void AddProperties(params UntypedProperty[] props)
            {
                foreach (UntypedProperty prop in props)
                {

                    _parent._properties.Add(new PropertyWrapper(prop, true));
                }
            }

            public void AddNonSuspendableProperties(params UntypedProperty[] props)
            {
                foreach (UntypedProperty prop in props)
                {

                    _parent._properties.Add(new PropertyWrapper(prop, false));
                }
            }
        }
        internal virtual void AddProperties(PropertiesCollector collector)
        {
            collector.AddNonSuspendableProperties(_rightToLeft);
            collector.AddProperties(_backColor, _foreColor, _dashStyle, _lineWidth,
                                    _style, _visible, _toolTip, _enabled);
        }


        internal ControlStyleInterface _controlStyle = null;
        internal ControlStyleInterface GetStyle()
        {
            return _controlStyle;
        }
        void SetStyle(ControlStyle style)
        {
            _controlStyle = CreateStyleFrom(style);
        }
        internal virtual ControlStyleInterface CreateStyleFrom(ControlStyle style)
        {
            ControlStyleInterface controlStyle;
            switch (style)
            {
                case ControlStyle.Standard:
                    controlStyle = new StandartControlStyle();
                    if (!_innerBorderInStandardStyle)
                        controlStyle = new InnerBorderInStandardStyleClass(controlStyle);
                    if (!_fixedBackColorINNonFlatStyles)
                        if (_fixDisabledBackColor)
                            controlStyle = new StandardControlStyleWithoutFixedBackColor(controlStyle);
                        else
                            controlStyle = new StandardControlStyleWithoutFixedBackColorAndWithoutFixedReadonlyBackColor(controlStyle);
                    break;
                case ControlStyle.Flat:
                    controlStyle = new FlatControlStyle();
                    if (_fixedDisabledReadonlyBackColorForFlatStyleNonTransparentBackground)
                        controlStyle = new FixedDisabledReadonlyBackColorForFlatStyleClassNonTransparent(controlStyle);
                    break;
                case ControlStyle.Sunken:
                    controlStyle = new SunkenControlStyle();
                    if (!_fixedBackColorINNonFlatStyles)
                        controlStyle = new UglyControlStyleWithoutFixedBackColor(controlStyle);
                    break;
                case ControlStyle.Raised:
                    controlStyle = new Ugly3DControlStyle();
                    if (!_fixedBackColorINNonFlatStyles)
                        controlStyle = new UglyControlStyleWithoutFixedBackColor(controlStyle);
                    break;
                default:
                    throw new Error.DidNotAnticipateThis();
            }
            return controlStyle;
        }

        internal virtual System.Drawing.Color _GetDefaultBackColor()
        {
            ControlBase cBase = Parent as ControlBase;
            if (cBase != null)
                return cBase.BackColor;
            Form f = Parent as Form;
            if (f != null)
                return f.BackColor;
            return _defaultBackColor;
        }
        internal virtual System.Drawing.Color _GetDefaultForeColor()
        {
            ControlBase cBase = Parent as ControlBase;
            if (cBase != null)
                return cBase.ForeColor;
            Form f = Parent as Form;
            if (f != null)
                return f.ForeColor;
            return _defaultForeColor;
        }
        static Color _defaultForeColor, _defaultBackColor;
        static ControlBase()
        {
            using (var c = new System.Windows.Forms.Control())
            {
                _defaultBackColor = c.BackColor;
                _defaultForeColor = c.ForeColor;
            }
        }

        internal virtual ControlStyle _GetDefaultControlStyle()
        {
            return ControlStyle.Raised;
        }

        internal virtual void _StyleChanged()
        {
            RepositionChildControls();
        }

        internal virtual void RepositionChildControls()
        {
        }

        protected override void OnResize(EventArgs e)
        {
            if (IsHandleCreated)
                RepositionChildControls();
            foreach (var setter in new[] { _deferredWidth, _deferredHeight })
                setter.Committed();
            base.OnResize(e);
            if (_displaySizeChanged != null)
                _displaySizeChanged(ClientSize, false);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            foreach (var setter in new[] { _deferredTop, _deferredLeft })
                setter.Committed();
        }

        SuspendedCommandClass _setInnerControlsColors;
        internal virtual void _ColorsChanged()
        {
            if (!_IsPrinting())
                _setInnerControlsColors.SetToRunOnResume();
        }

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            base.SetBoundsCore(x, y, width, height, specified);
            if (!_suspendAdvancedAnchor)
                _controlExtender.BoundsChanged(_moveWithoutResettingAdvancedAnchor);
        }

        internal Point OffsetByDeferredLocation(Point p)
        {
            p.Offset(_virtualParent.ToVirtual(Left) - DeferredLeft, Top - DeferredTop);
            return p;
        }

        [WizardOfOz.Witch.UI.Designer.LayoutCategory]
        public AdvancedAnchor AdvancedAnchor { get { return _controlExtender.AdvancedAnchor; } set { _controlExtender.AdvancedAnchor = value; } }

        internal Property<ControlStyle> _style;
        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        public virtual ControlStyle Style { get { return _style.Value; } set { _style.Value = value; } }
        Property<string> _toolTip;
        [WizardOfOz.Witch.UI.Designer.BehaviorCategory]
        public string ToolTip
        {
            get { return _toolTip.Value; }
            set
            {
                _toolTip.Value = (value ?? string.Empty).TrimEnd(' ');
            }
        }
        internal void AssertToolTip(string s)
        {
            Application.RaiseIdle(new EventArgs());
            AssertAppliedToolTip(s);
        }

        internal void AssertAppliedToolTip(string s)
        {
            _toolTipProvider.GetToolTip(this).ShouldBe(s);
        }


        SchemeHelper<ColorScheme> _colorScheme;

        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        public virtual ColorScheme ColorScheme
        {
            get { return _colorScheme.Value; }
            set
            {
                _colorScheme.Value = value;

            }
        }

        [System.Reflection.Obfuscation]
        bool ShouldSerializeForeColor()
        {
            return _foreColor.Value != _GetDefaultForeColor();
        }


        public override void ResetForeColor()
        {
            _foreColor.ResetToDefaultValue(_GetDefaultForeColor);
        }


        public virtual new System.Drawing.Color ForeColor
        {
            get
            {
                return _foreColor.Value;
            }
            set
            {
                _foreColor.Value = value;
                DoInvalidate();
            }
        }
        Property<System.Drawing.Color> _backColor;
        private Property<System.Drawing.Color> _foreColor;
        [System.Reflection.Obfuscation]
        public override void ResetBackColor()
        {
            _backColor.ResetToDefaultValue(_GetDefaultBackColor);
        }
        [System.Reflection.Obfuscation]
        bool ShouldSerializeBackColor()
        {
            return _backColor.Value != _GetDefaultBackColor();
        }


        public new virtual System.Drawing.Color BackColor
        {
            get
            {
                return _backColor.Value;
            }
            set
            {
                _backColor.Value = value;
                DoInvalidate();
            }
        }

        Property<System.Drawing.Color> _borderColor;
        Func<Color> _getBorderColor;
        Color DefaultGetBorderColor()
        {
            return _GetForeColor();
        }

        bool ShouldSerializeBorderColor()
        {
            if (_borderColorScheme == null)
                return BorderColor != ForeColor;
            return BorderColor != _borderColorScheme.ForeColor;
        }
        [System.Reflection.Obfuscation]
        void ResetBorderColor()
        {
            _getBorderColor = DefaultGetBorderColor;
            Invalidate();
        }
        [AppearanceCategory]
        public virtual System.Drawing.Color BorderColor
        {
            get
            {
                return _getBorderColor();
            }
            set
            {
                _borderColor.Value = value;
                _getBorderColor = delegate { return _borderColor.Value; };
                DoInvalidate();
            }
        }

        ColorScheme _borderColorScheme;
        public ColorScheme BorderColorScheme
        {
            get { return _borderColorScheme; }
            set
            {
                _borderColorScheme = value;
                if (value != null)
                    BorderColor = value.ForeColor;
            }
        }
  

        internal Property<System.Windows.Forms.RightToLeft> _rightToLeft;
        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        public new System.Windows.Forms.RightToLeft RightToLeft { get { return _rightToLeft.Value; } set { _rightToLeft.Value = value; } }

        protected override void OnParentRightToLeftChanged(EventArgs e)
        {
            base.OnParentRightToLeftChanged(e);
            DoInvalidate();
        }

        internal Property<bool> _rightToLeftLayout = new Property<bool>(true);
        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        public bool RightToLeftLayout { get { return _rightToLeftLayout.Value; } set { _rightToLeftLayout.Value = value; } }
        internal virtual bool GetRightToLeft()
        {
            switch (_rightToLeft.Value)
            {
                case RightToLeft.Inherit:
                    ControlBase cb = Parent as ControlBase;
                    if (cb != null)
                        return cb.GetRightToLeft();
                    else
                    {
                        System.Windows.Forms.Form frm = Parent as System.Windows.Forms.Form;
                        if (frm != null)
                        {
                            return frm.RightToLeft == RightToLeft.Yes ? true : false;
                        }
                        return false;
                    }
                case RightToLeft.Yes:
                    return true;
                default:
                    return false;
            }

        }
        [System.Reflection.Obfuscation(Exclude = true)]
        static bool ShouldSerializeRightToLeft()
        {
            return true;
        }

        bool _allowDrag = false;
        /// <summary>
        /// Gets or sets a value indicating whether data from this control can be dragged.
        /// </summary>
        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        public virtual bool AllowDrag { get { return _allowDrag; } set { _allowDrag = value; } }

        class DummyGridRowPainting : GridRowPainting
        {
            public bool IncludeUICues()
            {
                return true;
            }

            public bool UseOriginalControlColors()
            {
                return false;
            }

            public void AdjustForeColorForControls(Action<Color> adjust)
            {

            }

            public void AdjustBackColorForControls(Color originalColor, Action<Color> adjust)
            {

            }

            public void AdjustForeColorForColumns(Action<Color> adjust)
            {
            }

            public void AdjustBackColorForColumns(Action<Color> adjust)
            {
            }

            public bool IsHovered(ControlBase controlBase)
            {
                return controlBase._actionsForControls.IsHovered(controlBase);
            }
        }

        void PrintableControl.AddItemTo(AdvancedPrinter p)
        {
            ((PrintableControl)this).AddItemTo(p, new DummyGridRowPainting(), delegate { });
        }

        void PrintableControl.AddItemTo(AdvancedPrinter p, GridRowPainting gridRowPainting, Action<Rectangle, GridHotSpot> sendGridHotSpot)
        {
            if (!_visible.Value) return;
            var b = _controlExtender.AdjustBounds(GetCurrentBounds());
            DrawTo(p, b, gridRowPainting);
            sendGridHotSpot(b, new myGridHotSpot(this, Enabled, _GetProcessMnemonicForGridHotSpot()));
        }

        internal virtual Func<char, Action<Action>, bool> _GetProcessMnemonicForGridHotSpot()
        {
            return delegate { return false; };
        }

        internal virtual Rectangle GetCurrentBounds()
        {
            return new System.Drawing.Rectangle(
                _deferredLeft.GetValue(), _deferredTop.GetValue(),
                _deferredWidth.GetValue() + _clippedWidth, _deferredHeight.GetValue());
        }

        void DrawTo(AdvancedPrinter p, Rectangle r, GridRowPainting gridRowPainting)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (GetRightToLeft())
                p = new RightToLeftAdvancedPrinter(p, r);
            _InternalAddItemTo(p, r, new PaintingHelperClass(this, gridRowPainting));
        }

        bool _everDone = false;
        void PrintableControl.ForceRecalculationAndUpdateYourState(Suspended suspendManager, bool refreshData)
        {
            if (!_everDone)
            {
                _getDesignMode = () => false;
                _everDone = true;
            }

            _suspendManager = suspendManager;
            _binding.Apply();
            _controlExtender.ApplyBinding();
            InternalForceRecalculationAndUpdateYourState();
            if (refreshData)
                InternalForceDisplayData();
        }

        internal virtual void InternalForceDisplayData()
        {
        }

        class myGridHotSpot : GridHotSpot
        {
            ControlBase _parent;
            bool _enabled;
            string _toolTipText;
            Func<char, Action<Action>, bool> _processMnemonic;

            public myGridHotSpot(ControlBase parent, bool enabled, Func<char, Action<Action>, bool> processMnemonic)
            {
                _parent = parent;
                _enabled = enabled;
                _toolTipText = parent.ToolTip;
                _processMnemonic = processMnemonic;
            }

            public bool IgnoreMouseActivation
            {
                get { return _parent._IgnoreMouseActivationWhileDisabled() && !_enabled; }
            }

            public ControlBase GetControlBase()
            {
                return _parent;
            }

            public string GetToolTipText()
            {
                return _toolTipText;
            }

            public bool ProcessMnemonic(char c, Action<Action> gotoRowAndDo)
            {
                return _processMnemonic(c, gotoRowAndDo);
            }

            public bool Enabled
            {
                get { return _enabled; }
            }
        }

        internal virtual bool _IgnoreMouseActivationWhileDisabled()
        {
            return false;
        }

        internal virtual Cursor _GetHoveringCursor()
        {
            return null;
        }

        Suspended _oldSuspendManager;

        void PrintableControl.BeginGridPaintingState()
        {
            _doUnlessInGridPaintingState(
                () =>
                {
                    _doUnlessInGridPaintingState = delegate { };
                    _oldSuspendManager = _suspendManager;
                    _suspendManager = new DisablingSuspendManager();
                });
        }

        void PrintableControl.EndGridPaintingState()
        {
            var inGridPaintingState = true;
            _doUnlessInGridPaintingState(() => inGridPaintingState = false);
            if (!inGridPaintingState)
                return;
            _doUnlessInGridPaintingState = (action => action());
            _suspendManager = _oldSuspendManager;
            _oldSuspendManager = null;
        }

        public new object Tag
        {
            get { return base.Tag; }
            set
            {
                base.Tag = value;
                DoOnInnerControls(
                    delegate (System.Windows.Forms.Control control)
                    {
                        control.Tag = value;
                    });
            }
        }

        bool IControl.SuppressFocusedControlValidationInNavigation
        {
            get { return _SuppressFocusedControlValidationInNavigation; }
        }

        void IControl.ApplyLoadingTimeOnlyBindings()
        {
            _binding.Reset();
            _controlExtender.ResetBinding();
            _dragDropBinding.Apply();
            _LoadingTimeBindingApplied();
        }

        internal virtual void _LoadingTimeBindingApplied() { }

        void IControl.VisitSubForm(Action<ISubForm> visit)
        {
            VisitSubForm(visit);
        }

        void IControl.ProvideMaxToleratedFormWidthReduction(Action<int> provide)
        {
            _ProvideMaxToleratedFormWidthReduction(provide);
        }

        internal virtual void VisitSubForm(Action<ISubForm> visit) { }

        internal virtual void _ProvideMaxToleratedFormWidthReduction(Action<int> provide) { }

        internal virtual bool _SuppressFocusedControlValidationInNavigation
        {
            get { return false; }
        }

        int PrintableControl.GetPreferedColumnWidth(AdvancedPrinter p)
        {
            if (_controlExtender.LocationAnchored())
                return _virtualParent.ToVirtual(Left) + _GetPreferedWidth(p);
            return _virtualParent.ToVirtual(Left) + Width;
        }

        void PrintableControl.YouAreBeingPrinted()
        {
            _isForPrinting = true;
            CollectProperties();
            foreach (var p in _properties)
            {
                p.SimpleMode();
            }
            InternalYouAreBeingPrinted();
        }

        internal virtual void InternalYouAreBeingPrinted()
        {
        }

        internal virtual int _GetPreferedWidth(AdvancedPrinter p)
        {
            return Width;
        }

        Action<Action> _doUnlessInGridPaintingState = action => action();

        internal void _DoUnlessInGridPaintingState(Action doThis)
        {
            _doUnlessInGridPaintingState(doThis);
        }

        internal virtual void InternalForceRecalculationAndUpdateYourState()
        {
        }

        internal virtual void _InternalAddItemTo(AdvancedPrinter p, Rectangle a, PaintingHelper paintingHelper)
        {
        }

        internal interface PaintingHelper
        {
            bool IncludeUICues();
            LineDrawer CreateLineDrawer();
            ColorScheme GetColor();
            Color GetForeColor();
            Color GetBorderColor();
            bool IsHovered();
            Color AdjustBackColor(Color value);
        }

        internal class DummyPaintingHelper : PaintingHelper
        {
            ControlBase _control;

            public DummyPaintingHelper(ControlBase control)
            {
                _control = control;
            }

            public bool IncludeUICues()
            {
                return true;
            }

            public LineDrawer CreateLineDrawer()
            {
                return null;
            }

            public ColorScheme GetColor()
            {
                return _control.ColorScheme;
            }

            public Color GetForeColor()
            {
                return _control._GetForeColor();
            }

            public Color GetBorderColor()
            {
                return _control.ForeColor;
            }

            public bool IsHovered()
            {
                return !_control.DesignMode && _control.ClientRectangle.Contains(_control.PointToClient(MousePosition));
            }

            public Color AdjustBackColor(Color value)
            {
                _control._changeBackColor(color => value = color);
                return value;
            }
        }

        class PaintingHelperClass : PaintingHelper
        {
            ControlBase _parent;
            GridRowPainting _gridRowPainting;

            public PaintingHelperClass(ControlBase parent, GridRowPainting gridRowPainting)
            {
                _parent = parent;
                _gridRowPainting = gridRowPainting;
            }

            public bool IncludeUICues()
            {
                return _gridRowPainting.IncludeUICues();
            }

            public Color GetForeColor()
            {
                Color result = _parent._GetForeColor(_gridRowPainting.UseOriginalControlColors());
                _gridRowPainting.AdjustForeColorForControls(delegate (Color obj) { result = obj; });
                return _parent.GetStyle().GetForeground(result);
            }

            public Color GetBorderColor()
            {
                var result = _parent._getBorderColor != _parent.DefaultGetBorderColor ? _parent._getBorderColor() :
                    _parent._GetForeColor(_gridRowPainting.UseOriginalControlColors());
                _gridRowPainting.AdjustForeColorForControls(delegate (Color obj) { result = obj; });
                return _parent.GetStyle().GetForeground(result);
            }

            public bool IsHovered()
            {
                return _gridRowPainting.IsHovered(_parent);
            }

            public Color AdjustBackColor(Color value)
            {
                _gridRowPainting.AdjustBackColorForControls(value, color => value = color);
                return value;
            }

            Color GetBackColor()
            {
                Color result = _parent._GetBackColor(_gridRowPainting.UseOriginalControlColors());
                _gridRowPainting.AdjustBackColorForControls(result, delegate (Color obj) { result = obj; });
                return result;
            }

            public ColorScheme GetColor()
            {
                return new ColorScheme(GetBorderColor(), GetBackColor());
            }

            public LineDrawer CreateLineDrawer()
            {
                return new LineDrawer(GetColor(), _parent.internalLineWidth, _parent.InternalLineDashStyle);
            }
        }

        internal void AddItemToForTesting(AdvancedPrinter p, Rectangle a)
        {
            DrawTo(p, a, new DummyGridRowPainting());
        }

        internal Color _GetForeColor()
        {
            return _GetForeColor(false);
        }

        internal virtual Color _GetForeColor(bool getOriginal)
        {
            Color color = ForeColor;
            if (!getOriginal)
                _changeForeColor(delegate (Color obj) { color = obj; });
            color = GetStyle().GetForeground(color);
            return color;
        }
        internal virtual Color GetBackColorForInnerControls()
        {
            var color = _GetBackColor();
            if (color == Color.Transparent)
            {
                if (ColorScheme != null && ColorScheme.GetBackColorIgnoreTransparentBackground() != Color.Transparent)
                    return ColorScheme.GetBackColorIgnoreTransparentBackground();
                return _GetDefaultBackColor();
            }
            return color;

        }
        internal virtual Color GetForeColorForInnerControls()
        {
            return _GetForeColor();

        }

        internal Color _GetBackColor()
        {
            return _GetBackColor(false);
        }
        internal virtual Color _GetBackColor(bool getOriginal)
        {
            Color color = BackColor;
            if (!getOriginal)
                _changeBackColor(delegate (Color obj) { color = obj; });

            color = GetStyle().GetBackground(color, Enabled, _virtualParent.AllowFixedBackColorForDisabledControl);
            return color;
        }

        internal System.Drawing.Drawing2D.DashStyle InternalLineDashStyle { get { return _dashStyle.Value; } set { _dashStyle.Value = value; DoInvalidate(); } }
        Property<int> _lineWidth = new Property<int>(1);
        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        internal int internalLineWidth
        {
            get { return _lineWidth.Value; }
            set
            {
                _lineWidth.Value = value;
                _LineWidthChanghed();
                DoInvalidate();
            }
        }

        internal virtual void _LineWidthChanghed()
        {
        }

        Property<bool> _visible;
        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        public new virtual bool Visible { get { return _visible.Value; } set { _visible.Value = value; } }
        /// <summary>
        /// Gets a value indicating whether the Control should be visible.
        /// </summary>
        /// <remarks>This property will return a value according to the Visible and BindVisible events ignoring bound controls.
        /// Should be used in a scenario where you have a control on a non visible tab and you want to know if it would be visible if the tab was visible
        /// </remarks>
        public virtual bool Available
        {
            get
            {
                if (!_visible.GetValueUnlocked()) return false;
                if (_controlBindingControl.Control != null && !_controlBindingControl.Control.Available) return false;
                if (!_virtualParent.Available) return false;
                if (Parent is ControlBase && !((ControlBase)Parent).Available) return false;
                return true;
            }
        }

        internal Property<bool> _enabled;
        [WizardOfOz.Witch.UI.Designer.AppearanceCategory]
        public new virtual bool Enabled { get { return _enabled.Value; } set { _enabled.Value = value; } }

        bool _fixedBackColorINNonFlatStyles = false;
        bool _fixDisabledBackColor = true;
        bool _innerBorderInStandardStyle = true;
        /// <summary>
        /// When set to true, when ever the <see cref="ControlStyle.Raised"/> or <see cref="ControlStyle.Sunken"/> <see cref="ControlStyle"/> are used, the <see cref="BackColor"/> will be fixed to <see cref="System.Drawing.SystemColors.Control"/>
        /// </summary>
        public bool FixedBackColorInNonFlatStyles
        {
            get
            {
                return _fixedBackColorINNonFlatStyles;
            }
            set
            {
                _fixedBackColorINNonFlatStyles = value;
                SetStyle(Style);
            }
        }
        public bool InnerBorderInStandardStyle
        {
            get { return _innerBorderInStandardStyle; }
            set
            {
                _innerBorderInStandardStyle = value;
                SetStyle(Style);
            }
        }
        public bool FixedDisabledBackColor
        {
            get
            {
                return _fixDisabledBackColor;
            }
            set
            {
                _fixDisabledBackColor = value;
                SetStyle(Style);
            }
        }
        bool _fixedDisabledReadonlyBackColorForFlatStyleNonTransparentBackground;
        public bool FixedDisabledReadonlyBackColorForFlatStyleNonTransparentBackground
        {
            get
            {
                return _fixedDisabledReadonlyBackColorForFlatStyleNonTransparentBackground;
            }
            set
            {
                _fixedDisabledReadonlyBackColorForFlatStyleNonTransparentBackground = value;
                SetStyle(Style);
            }
        }

        Property<System.Drawing.Drawing2D.DashStyle> _dashStyle = new Property<System.Drawing.Drawing2D.DashStyle>(
            System.Drawing.Drawing2D.DashStyle.Solid);

        internal bool _wasActivatedForUI = false;

        void WizardOfOz.Witch.UI.IControl.Init(ActionsForControls actionsForControls, Suspended suspendManager)
        {
            _actionsForControls = actionsForControls;
            _suspendManager = suspendManager;
        }
        void WizardOfOz.Witch.UI.IControl.ActivateUI()
        {
            if (_wasActivatedForUI) return;

            CollectProperties();

            foreach (PropertyWrapper wrapper in _properties)
            {
                wrapper.SetSuspendManager(() => _suspendManager, delegate { return !this.IsDisposed; });
            }
            _ApplySpecialAnchor();
            _originalBounds = Bounds;
            _ActivateUI();
            RepositionChildControls();
            _wasActivatedForUI = true;
            _toolTipProvider.RightToLeft = GetRightToLeft();

            DoOnInnerControls(
                delegate (System.Windows.Forms.Control control)
                {
                    control.MouseMove += (sender, args) => _actionsForControls.SetHoveredControl(this);
                    control.DoubleClick += (o, eventArgs) => OnDoubleClick(eventArgs);
                    control.MouseClick += (sender, args) => OnMouseClick(args);
                    if (_AllowMouseToggleOfActiveGridRowInSelectOnClickGridColumn())
                    {
                        control.MouseDown += (sender, args) =>
                                             {
                                                 base.OnMouseDown(args);
                                                 if (args.Button == MouseButtons.Left)
                                                     NotifyMouseDownToParent(RegisterMouseUpAction);
                                             };
                        control.MouseUp += (sender, args) =>
                        {
                            base.OnMouseUp(args);
                            InvokeRegisteredMouseUpAcion();
                        };
                        control.MouseCaptureChanged += (sender, args) =>
                                                       {
                                                           if (!control.Capture)
                                                               InvokeRegisteredMouseUpAcion();
                                                       };
                    }
                });
        }

        internal virtual bool _AllowMouseToggleOfActiveGridRowInSelectOnClickGridColumn()
        {
            return true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            InternalOnMouseMove(e);
            base.OnMouseMove(e);

            if (e.Button == MouseButtons.Left && _startDragIfMouseMoveOusideThisArea != Rectangle.Empty && !_startDragIfMouseMoveOusideThisArea.Contains(e.Location))
            {
                try
                {
                    _DoDragDrop(o => { });
                }
                finally
                {
                    _startDragIfMouseMoveOusideThisArea = Rectangle.Empty;
                }
            }
        }

        internal void InternalOnMouseMove(MouseEventArgs e)
        {
            InternalVirtualOnMouseMove(e);
        }

        internal virtual void InternalVirtualOnMouseMove(MouseEventArgs e)
        {
            if (Enabled)
                _actionsForControls.SetHoveredControl(this);
        }

        bool _isForPrinting;
        internal bool _IsPrinting()
        {
            return _isForPrinting && !DesignMode;
        }

        internal virtual void _ActivateUI()
        {
        }
        bool _rightToLeftForm;

        internal virtual void _ApplySpecialAnchor()
        {
            _controlExtender.ApplySpecialAnchor(new myControlForAdvancedAnchor(this), manager => _controlBindingControl.ApplySpecialAnchor(manager, Bounds, () => _virtualParent.ApplySpecialAnchor(this.Parent, manager)));
            var f = FindForm();
            if (f != null && f.RightToLeft == RightToLeft.Yes)
                _rightToLeftForm = true;
        }

        void WizardOfOz.Witch.UI.IControl.DoOnColumnControl(Action<ColumnControl> what)
        {
            _DoOnColumn(what);
        }

        bool WizardOfOz.Witch.UI.IControl.AreYou(Control control)
        {
            return control == this;
        }

        void WizardOfOz.Witch.UI.IControl.ResetDeferredBounds()
        {
            if (FindForm() == null || FindForm().RightToLeft != RightToLeft.Yes || _controlExtender.UseRelativeDeferredLeftInRightToLeft())
                _deferredLeft.Reset();
            foreach (var b in new[] { _deferredTop, _deferredWidth, _deferredHeight })
                b.Reset();
        }

        void WizardOfOz.Witch.UI.IControl.AddDataViewElements(DataViewElementsCollector toMe)
        {
            _AddDataViewElements(toMe);
        }

        internal virtual void _AddDataViewElements(DataViewElementsCollector toMe)
        {
        }

        void WizardOfOz.Witch.UI.IControl.BindProperties(PropertyBinder binder)
        {
            OnLoad();
            _BindProperties(binder);
        }
        public event Action BindProperties { add { _controlExtender.BindProperties += value; } remove { _controlExtender.BindProperties -= value; } }
        protected virtual void OnLoad()
        {
            if (Load != null)
                Load();
        }

        public event Action Load;

        void IControl.BlockUIChanges()
        {
            ((PrintableControl)this).BeginGridPaintingState();
        }

        void IControl.UnblockUIChanges()
        {
            ((PrintableControl)this).EndGridPaintingState();
        }

        internal virtual void AddToFlow(FlowBuilderForForm flowBuilder)
        {
        }

        internal virtual void _DoOnColumn(Action<ColumnControl> doOnColumn)
        {

        }

        internal ActionsForControls _actionsForControls = new NullActionsForControls();

        internal void InvokeMouseMovementCommandForTesting(Command command)
        {
            _actionsForControls.HandleCommand(command, new object[0], this,
                handling => handling.OnCurrentlyActiveTask(invoking => invoking.Invoke(() => { })));
        }

        internal void _Raise(Firefly.Box.Command eventBuilder, params object[] args)
        {
            _actionsForControls.HandleCommand(eventBuilder, args, this,
                handling => handling.OnThisTask(invoking => invoking.Raise(true)));
        }

        internal void _RaiseSpecialTabControlExpand()
        {
            _actionsForControls.HandleCommand(Command.Expand, new object[0], this,
                handling => handling.OnThisTask(invoking => invoking.Raise(e => !e.ParkedControlIs(this), true)));
        }

        internal bool _InvokeActionAndReturnTrueIfHandled(Firefly.Box.Command command)
        {
            bool doDefault = false;
            _actionsForControls.HandleCommand(command, new object[0], this,
                handling => handling.OnThisTask(invoking => invoking.Invoke(() => { doDefault = true; })));
            return !doDefault;
        }

        internal void _BringFocusToForm(DoWhenFormIsFocused andDo)
        {
            _RunActionWhichMayCauseAFocusChange(focusForm => focusForm(andDo));
        }

        internal void _RunActionWhichMayCauseAFocusChange(Action<FocusForm> action)
        {
            _actionsForControls.RunActionWhichMayCauseAFocusChange(action, this, false);
        }

        internal virtual void _RequestFocus(Action runIfSucceeded)
        {
            _BringFocusToForm(delegate { runIfSucceeded(); });
        }

        internal virtual void _RequestFocusAndDoWhenFormIsActive(Action doThis)
        {
            _BringFocusToForm(delegate { doThis(); });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _ColorsChanged();
            if (_ContainsTransparentParts() && !IsPaintedByParent())
            {
                var parent = Parent as ParentOfTransparentControls;
                if (parent != null)
                    parent.AddTransparentChildControl(this);
            }
            InitControlForTesting(this.DesignMode);
        }

        internal void InitControlForTesting(bool designMode)
        {
            if (designMode)
                _InitDesignMode();
            else
                _InitRuntime();
            RepositionChildControls();
            _getDesignMode = delegate { return designMode; };
        }

        internal Func<bool> _getDesignMode;

        internal virtual void _InitDesignMode()
        {
        }

        internal void AutoSkipToNextControl(char lastChar)
        {
            _actionsForControls.AutoSkipToNextControl(lastChar);
        }

        internal void ChangeContextForTesting(int i)
        {
            _attachableControl.VisibleLayerChanged(i);
        }

        internal Suspended _suspendManager = DummySuspendManager.Instance;

        internal virtual void _BindProperties(PropertyBinder binder)
        {
            _tabIndexBinding.Bind(binder);
            _controlExtender._BindProperties(binder);
            _binding.Bind(binder);
        }

        internal virtual void _InitRuntime()
        {
        }
        ControlBinding _controlBindingControl = ControlBinding.Default;
        /// <summary>
        /// Determines to which control this control is bound to.
        /// </summary>
        public ControlBinding BoundTo
        {
            get
            {
                return _controlBindingControl;
            }
            set
            {
                _controlBindingControl.Detach(this);
                _controlBindingControl = value ?? ControlBinding.Default;
                _controlBindingControl.Attach(this);
                if (Parent != null)
                    Parent.PerformLayout(this, UI.ZOrder.CausesZOrderChange);

            }
        }

        internal Color _GetBackColorOfBoundToControl()
        {
            return _controlBindingControl.GetBackColor();
        }

        internal void DoInvalidate()
        {
            if (!_IsPrinting())
            {
                DoInvalidate(this.ClientRectangle);
                DoOnInnerControls(delegate (Control control) { control.Invalidate(); });
            }
        }

        bool IsPaintedByParent()
        {
            if (PaintByParentAllowed())
            {
                var parent = this.Parent as CanPaintChildControls;
                return parent != null && parent.PaintsChildControls();
            }
            return false;
        }

        internal virtual bool BlockPaintByParent()
        {
            return false;
        }

        bool _inWmPrint;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x317 || m.Msg == 0x318) // WM_PRINT
            {
                _inWmPrint = true;
                try
                {
                    base.WndProc(ref m);
                }
                finally
                {
                    _inWmPrint = false;
                }
                return;
            }
            base.WndProc(ref m);
        }

        internal virtual bool PaintOnlyBackgroundOnWmPrint()
        {
            return false;
        }

        #region Background Drawing
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            if (_inWmPrint && PaintOnlyBackgroundOnWmPrint())
            {
                PaintOnlyMyBackground(e);
                return;
            }
            if (!IsPaintedByParent())
            {
                if (e.ClipRectangle.IsEmpty) return;

                _childControlPainter.Paint(this, e,
                    delegate (System.Drawing.Graphics g, Rectangle clipRectangle)
                    {
                        Action<System.Drawing.Graphics> paint =
                            delegate (System.Drawing.Graphics graphics)
                            {
                                PaintWithBackground(new System.Windows.Forms.PaintEventArgs(graphics, clipRectangle));
                            };
                        if (UseDoubleBuffering())
                        {
                            using (BufferedGraphics bg = BufferedGraphicsManager.Current.Allocate(g, ClientRectangle))
                            {
                                paint(bg.Graphics);
                                bg.Render();
                            }
                        }
                        else
                            paint(g);
                    }, false, true, false);
            }
        }

        void PaintWithBackground(System.Windows.Forms.PaintEventArgs e)
        {
            if (_ContainsTransparentParts())
                _backgroundPainter.PaintWithBackground(new GraphicsWrapperClass(e, ClientRectangle.Size), false);
            else
                PaintUsingEventArgs(e);
        }

        void PaintUsingEventArgs(System.Windows.Forms.PaintEventArgs e)
        {
            UseGUIDrawer(e,
                delegate (IPrinterWriter pw, StringMeasurer measurer)
                {
                    PrivatePaint(pw, new System.Drawing.Rectangle(0, 0, ClientRectangle.Width, ClientRectangle.Height), measurer);
                });
        }

        internal void InvokePaintForTesting()
        {
            PaintUsingEventArgs(new PaintEventArgs(System.Drawing.Graphics.FromImage(new Bitmap(Bounds.Width, Bounds.Height)), ClientRectangle));
        }

        internal static void UseAdvancedPrinter(PaintEventArgs e, Action<AdvancedPrinter> paint)
        {
            UseGUIDrawer(e, delegate (IPrinterWriter obj, StringMeasurer measurer) { UseAdvancedPrinter(obj, paint, measurer); });
        }

        static void UseAdvancedPrinter(IPrinterWriter pw, Action<AdvancedPrinter> paint, StringMeasurer measurer)
        {
            paint(new AdvancedPrinterClass(measurer, int.MaxValue, false, false,
                (area, item, resizeToFitInContainer) =>
                {
                    item(pw, area);
                    return new DummyItemInPrintCommand(area);
                }));
        }

        static void UseGUIDrawer(PaintEventArgs e, Action<IPrinterWriter, StringMeasurer> paint)
        {
            using (GDI.Graphics gdi = GDI.Graphics.FromGraphics(e.Graphics))
            {
                using (var g = new GUIDrawer(gdi))
                    paint(new ClippingDecorator(e.ClipRectangle, g), g);
            }
        }

        internal virtual bool UseDoubleBuffering()
        {
            return false;
        }
        internal virtual bool _ContainsTransparentParts()
        {
            return _controlStyle != null && Graphics.IsTransperantOrEmpty(BackColor) && _controlStyle.CanBeTransparent();
        }
        #endregion
        internal void AssertRealVisible(bool visible)
        {
            Should.Equal(visible, base.Visible, "visible");
        }

        internal void AssertBounds(int left, int top, int width, int height)
        {
            AssertBounds(this, left, top, width, height);
        }
        internal void AssertColors(Color foreColor, Color backColor)
        {
            Should.Equal(foreColor, _GetForeColor(), "Fore Color");
            Should.Equal(backColor, _GetBackColor(), "Back Color");
        }
        internal static void AssertBounds(System.Windows.Forms.Control control,
                                          int left, int top, int width, int height)
        {
            Should.Equal(left, control.Location.X, "Left");
            Should.Equal(top, control.Location.Y, "Top");
            Should.Equal(width, control.Bounds.Width, "Width");
            Should.Equal(height, control.Bounds.Height, "Height");
        }
        internal myAttachableControl _attachableControl;
        internal class myAttachableControl : AttachableControl
        {
            Dictionary<AttachedControl, int> _controlsToLayers = new Dictionary<AttachedControl, int>();
            Dictionary<int, List<AttachedControl>> _layersToControls = new Dictionary<int, List<AttachedControl>>();
            int _visibleLayer = -1;
            ControlBase _parent;

            public myAttachableControl(ControlBase parent)
            {
                _parent = parent;
                _layersToControls.Add(-1, new List<AttachedControl>());
            }

            public void Detach(AttachedControl control)
            {
                int layer;
                if (_controlsToLayers.TryGetValue(control, out layer))
                {
                    _controlsToLayers.Remove(control);
                    _layersToControls[layer].Remove(control);
                }
            }
            public void Attach(AttachedControl control, int context)
            {
                Detach(control);
                _controlsToLayers.Add(control, context);
                List<AttachedControl> controlInLayer;
                if (!_layersToControls.TryGetValue(context, out controlInLayer))
                {
                    controlInLayer = new List<AttachedControl>();
                    _layersToControls.Add(context, controlInLayer);
                }
                controlInLayer.Add(control);
                control.SetVisible((context == -1 || _visibleLayer == context) && _parent._ShowAttachedControls());
                control.SetEnabled(_parent._enabled.Value);
            }

            public void ApplySpecialAnchorOnAttached(SpecialAnchorManager specialAnchorManager, Rectangle boundControlBounds, Action doDefault)
            {
                _parent._ApplySpecialAnchorOnAttached(specialAnchorManager, boundControlBounds,
                    () => _parent._controlBindingControl.ApplySpecialAnchor(specialAnchorManager, boundControlBounds, doDefault));
            }

            public bool SendBoundControlsToFrontOfZOrder()
            {
                return _parent._SendBoundControlsToFrontOfZOrder();
            }

            public void DoOnAttachedControls(Action<AttachedControl> action)
            {
                foreach (var c in _controlsToLayers.Keys)
                    action(c);
            }

            public void VisibleChanged(bool visible)
            {
                foreach (var control in _layersToControls[-1])
                    control.SetVisible(visible);
                if (_visibleLayer != -1)
                    foreach (var control in _layersToControls[_visibleLayer])
                        control.SetVisible(_parent._ShowAttachedControls());
            }
            public void VisibleLayerChanged(int newVisibleLayer)
            {
                if (newVisibleLayer == _visibleLayer)
                    return;
                if (!_layersToControls.ContainsKey(newVisibleLayer))
                    _layersToControls.Add(newVisibleLayer, new List<AttachedControl>());
                if (_visibleLayer != -1)
                    foreach (AttachedControl control in _layersToControls[_visibleLayer])
                        control.SetVisible(false);
                _visibleLayer = newVisibleLayer;
                foreach (AttachedControl control in _layersToControls[_visibleLayer])
                    control.SetVisible(_parent._ShowAttachedControls());
            }

            public void EnabledChanged(bool value)
            {
                DoOnAttachedControls(control => control.SetEnabled(value));
            }

            public void LeftChanged(int delta)
            {
                DoOnAttachedControls(control => control.MoveLeft(delta));
            }

            public void SetAutoTabOrder(Func<int> nextTabIndex)
            {
                var controls = new SortedDictionary<int, AttachedControl>();
                DoOnAttachedControls(control => control.VisitControl(c => controls.Add(c.TabIndex, control)));
                foreach (var item in controls)
                    item.Value.SetTabIndex(nextTabIndex());
            }

            public void TopChanged(int delta)
            {
                DoOnAttachedControls(control => control.MoveTop(delta));
            }

            public bool IsPseudoContainer()
            {
                return _parent._IsPseudoContainer();
            }

            public void ForEachLayer(Action<AttachedControl[]> doThis)
            {
                var sortedLayers = new int[_layersToControls.Count];
                _layersToControls.Keys.CopyTo(sortedLayers, 0);
                Array.Sort(sortedLayers);
                foreach (var l in sortedLayers)
                    doThis(_layersToControls[l].ToArray());
            }
        }

        internal virtual bool _ShowAttachedControls()
        {
            return _visible.Value;
        }

        internal virtual bool _SendBoundControlsToFrontOfZOrder()
        {
            return _controlBindingControl.SendBoundControlsToFrontOfZOrder();
        }

        internal virtual void _ApplySpecialAnchorOnAttached(SpecialAnchorManager specialAnchorManager, Rectangle boundControlBounds, Action doDefault)
        {
            doDefault();
        }

        internal AttachableControl GetAttachableControl()
        {
            return _attachableControl;
        }
        internal class MouseClickListener
        {
            ControlBase _parent;
            Action _defaultClickAction;
            Dictionary<int, Area> _areas = new Dictionary<int, Area>();
            int _currentHoveredAreaIndex = -1;

            class Area
            {
                System.Drawing.Rectangle _rectangle;
                Action _command;
                public Action MouseEnter;
                public Action MouseLeave;

                public Area(Rectangle rectangle, Action command, Action mouseEnter, Action mouseLeave)
                {
                    _rectangle = rectangle;
                    _command = command;
                    MouseEnter = mouseEnter;
                    MouseLeave = mouseLeave;
                }

                public bool Process(MouseEventArgs e)
                {
                    if (_rectangle.Contains(e.X, e.Y))
                    {
                        _command();
                        return true;
                    }
                    return false;
                }

                public bool IsHovered(MouseEventArgs e)
                {
                    return _rectangle.Contains(e.X, e.Y);
                }
            }
            public MouseClickListener(ControlBase parent, Action defaultClickAction)
            {
                _parent = parent;
                _defaultClickAction = defaultClickAction;
                MouseEventHandler clickHandler =
                    delegate (object sender, MouseEventArgs e)
                    {
                        foreach (Area area in _areas.Values)
                        {
                            if (area.Process(e))
                            {
                                return;
                            }
                        }
                        _defaultClickAction();
                    };
                _parent.MouseDown += clickHandler;
                _parent.MouseLeave += delegate (object sender, EventArgs e) { SetHoveredArea(-1); };
                _parent.MouseMove +=
                    delegate (object sender, MouseEventArgs e)
                    {
                        foreach (KeyValuePair<int, Area> pair in _areas)
                        {
                            if (pair.Value.IsHovered(e))
                            {
                                SetHoveredArea(pair.Key);
                                return;
                            }
                        }
                        SetHoveredArea(-1);
                    };
            }

            void SetHoveredArea(int areaIndex)
            {
                if (areaIndex != _currentHoveredAreaIndex)
                {
                    if (_currentHoveredAreaIndex != -1 && _areas.ContainsKey(_currentHoveredAreaIndex))
                        _areas[_currentHoveredAreaIndex].MouseLeave();
                    if (areaIndex != -1)
                        _areas[areaIndex].MouseEnter();
                    _currentHoveredAreaIndex = areaIndex;
                }
            }

            public void AddMouseSensitiveLocation(Rectangle referenceArea, Rectangle mouseClickArea,
                Action whatToDo, int index, Action mouseEnter, Action mouseLeave)
            {
                mouseClickArea.Offset(-referenceArea.X, -referenceArea.Y);
                _areas[index] = new Area(mouseClickArea,
                    delegate ()
                    {
                        _parent.Capture = false;
                        whatToDo();
                    }, mouseEnter, mouseLeave);
            }

            public void AddRightToLeftSensetiveLocation(Rectangle referenceArea, int x, int y, int width, int height, Action whatToDo, int index)
            {
                Rectangle mouseClickArea = new Rectangle(x, y, width, height);
                if (_parent._rightToLeft.Value == RightToLeft.Yes)
                    mouseClickArea.X = referenceArea.Right - mouseClickArea.Width - (mouseClickArea.X - referenceArea.X);
                //mouseClickArea.Offset(referenceArea.X + referenceArea.Width - mouseClickArea.Width - mouseClickArea.X * 2, 0);
                AddMouseSensitiveLocation(referenceArea, mouseClickArea, whatToDo, index, delegate () { }, delegate () { });
            }

            public void Clear()
            {
                _areas.Clear();
            }

            internal bool IsArea(MouseEventArgs e)
            {

                foreach (KeyValuePair<int, Area> pair in _areas)
                {
                    if (pair.Value.IsHovered(e))
                    {

                        return true;
                    }
                }
                return false;
            }
        }

        internal virtual void InternalPaint(IPrinterWriter printerWriter, AdvancedPrinter advancedPrinter, Rectangle area, StringMeasurer measurer)
        {
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;

            DrawTo(advancedPrinter, area, new DummyGridRowPainting());
        }

        bool _allowPaintByParent = true;

        public bool AllowPaintByParent
        {
            get { return _allowPaintByParent; }
            set { _allowPaintByParent = value; }
        }

        bool CanBePaintedByParent.PaintByParentAllowed()
        {
            return PaintByParentAllowed();
        }

        bool PaintByParentAllowed()
        {
            return _allowPaintByParent && !DesignMode && !BlockPaintByParent();
        }

        void CanBePaintedByParent.Paint(IPrinterWriter w, Rectangle area, StringMeasurer measurer)
        {
            PrivatePaint(w, area, measurer);
        }

        void CanBePaintedByParent.PaintOnTopOfOtherControls(IPrinterWriter w, Rectangle area, StringMeasurer measurer)
        {
            _PaintOnTopOfOtherControls(w, area, measurer);
        }

        internal virtual void _PaintOnTopOfOtherControls(IPrinterWriter w, Rectangle area, StringMeasurer measurer) { }

        void PrivatePaint(IPrinterWriter w, Rectangle area, StringMeasurer measurer)
        {
            UseAdvancedPrinter(w,
                delegate (AdvancedPrinter ap)
                {
                    InternalPaint(w, ap, area, measurer);
                }, measurer);
        }

        void TransparencySupportingControl.Paint(System.Windows.Forms.PaintEventArgs e)
        {
            PaintUsingEventArgs(e);
        }

        void ParentOfTransparentControls.PaintAsBackground(System.Windows.Forms.PaintEventArgs e)
        {
            PaintWithBackground(e);
        }

        void ParentOfTransparentControls.AddTransparentChildControl(System.Windows.Forms.Control childControl)
        {
            _backgroundPainter.AddTransparentChildControl(childControl);
        }
        TransparentPainter _backgroundPainter;

        internal void PaintOnlyMyBackground(PaintEventArgs e)
        {
            _backgroundPainter.PaintWithBackground(new GraphicsWrapperClass(e, ClientRectangle.Size), true);
        }

        internal virtual void RemoveDisplayedDataFromControls()
        {
        }

        int _clippedWidth;
        BoundsSetter _deferredLeft, _deferredTop, _deferredWidth, _deferredHeight;
        CreateHandleListener _createHandleListener = delegate { };

        protected override void CreateHandle()
        {
            System.Windows.Forms.Form form = this.FindForm();
            _createHandleListener(this.DesignMode, form != null ? form.RightToLeftLayout : false, false);
            base.CreateHandle();
        }

        [System.ComponentModel.DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Browsable(false)]
        public int DeferredLeft
        {
            set { _deferredLeft.SetNewValue(value); }
            get { return _deferredLeft.GetValue(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Browsable(false)]
        public int DeferredTop
        {
            set { _deferredTop.SetNewValue(value); }
            get { return _deferredTop.GetValue(); }
        }
        [System.ComponentModel.DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Browsable(false)]
        public int DeferredWidth
        {
            set { _deferredWidth.SetNewValue(value); }
            get { return _deferredWidth.GetValue(); }
        }
        [System.ComponentModel.DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Browsable(false)]
        public int DeferredHeight
        {
            set { _deferredHeight.SetNewValue(value); }
            get { return _deferredHeight.GetValue(); }
        }

        PropertyDescriptor PropertyDecorator.DecorateProperty(PropertyDescriptor prop)
        {
            return _DecorateProperty(_colorScheme.DecorateProperty(prop));
        }


        internal virtual PropertyDescriptor _DecorateProperty(PropertyDescriptor prop)
        {
            return prop;
        }

        void AttachedControl.SetVisible(bool value)
        {
            if (!value)
                _visible.LockValue(false);
            else
                _visible.UnlockValue();
        }
        bool _wasEnabledBound = false;
        void AttachedControl.SetEnabled(bool value)
        {
            if (!_wasEnabledBound)
            {
                if (!value)
                    _enabled.LockValue(false);
                else
                    _enabled.UnlockValue();
            }

        }

        void AttachedControl.MoveLeft(int delta)
        {
            DoWhileMovingWithoutResettingsAdvancedAnchor(() => Left += delta);
            if (!_IsPseudoContainer())
                _attachableControl.DoOnAttachedControls(c => c.MoveLeft(delta));
        }

        void AttachedControl.SetTabIndex(int tabIndex)
        {
            this.TabIndex = tabIndex;
        }

        void AttachedControl.MoveTop(int delta)
        {
            DoWhileMovingWithoutResettingsAdvancedAnchor(() => Top += delta);
            if (!_IsPseudoContainer())
                _attachableControl.DoOnAttachedControls(c => c.MoveTop(delta));
        }

        internal Rectangle _originalBounds;
        bool AttachedControl.BoundsIntersectWith(Rectangle bounds)
        {
            return bounds.Contains(_originalBounds) || _originalBounds.IntersectsWith(bounds);
        }

        internal virtual void ExpandOnDoubleClick(Action<bool> expand)
        {
        }



        Binding _dragDropBinding;
        public event BindingEventHandler<BooleanBindingEventArgs> BindAllowDrag
        {
            add { _dragDropBinding.Add("AllowDrag", () => AllowDrag, x => _actionsForControls.DoOnUIThread(() => AllowDrag = x), value); }
            remove { _dragDropBinding.Remove("AllowDrag", value); }
        }
        public event BindingEventHandler<BooleanBindingEventArgs> BindAllowDrop
        {
            add { _dragDropBinding.Add("AllowDrop", () => AllowDrop, x => _actionsForControls.DoOnUIThread(() => AllowDrop = x), value); }
            remove { _dragDropBinding.Remove("AllowDrop", value); }
        }

        bool _bindVisibleCalculated;
        internal bool _isVisibleBound;
        public event BindingEventHandler<BooleanBindingEventArgs> BindVisible
        {
            add
            {
                _isVisibleBound = true;
                _binding.Add("Visible", () => Visible,
                    x =>
                    {
                        _bindVisibleCalculated = true;
                        Visible = x;
                    }, value);
            }
            remove { _binding.Remove("Visible", value); }
        }

        internal bool _IsVisibleIncludingBindVisible()
        {
            if (!_bindVisibleCalculated)
                return _binding.EvaluateBoolBinding("Visible", Visible);
            return Visible;
        }

        internal bool _IsHiddenButNotDueToBoundToControlLayer()
        {
            return !_visible.GetValueUnlocked();
        }

        public event BindingEventHandler<StringBindingEventArgs> BindToolTip
        {
            add { _binding.Add("ToolTip", () => ToolTip, x => ToolTip = x, value, false); }
            remove { _binding.Remove("ToolTip", value); }
        }

        public event BindingEventHandler<BooleanBindingEventArgs> BindEnabled
        {
            add { _binding.Add("Enabled", () => Enabled, x => Enabled = x, value); _wasEnabledBound = true; _enabled.UnlockValue(); }
            remove { _binding.Remove("Enabled", value); }
        }

        internal bool _boundsBindingCalculated = false;
        internal bool _ReizeToFitPrintedContainer() { return _IsPrinting() && _boundsBindingCalculated; }

        Binding _tabIndexBinding;
        public event BindingEventHandler<IntBindingEventArgs> BindTabIndex
        {
            add
            {
                _tabIndexBinding.Add("TabIndex", () => TabIndex,
              x =>
              {
                  if (x != TabIndex)
                  {
                      TabIndex = x;
                      _actionsForControls.ControlTabIndexChanged();
                  }
              }, value);
            }
            remove { _tabIndexBinding.Remove("TabIndex", value); }
        }

        internal virtual void _WidthChangedDueToBindWidth(int newWidth, Action<int> setAction)
        {
            setAction(newWidth);
        }

        public event BindingEventHandler<ColorSchemeBindingEventArgs> BindColorScheme
        {
            add { _binding.Add("ColorScheme", () => ColorScheme, x => ColorScheme = x, value); }
            remove { _binding.Remove("ColorScheme", value); }
        }
        public event BindingEventHandler<ColorSchemeBindingEventArgs> BindBorderColorScheme
        {
            add { _binding.Add("BorderColorScheme", () => ColorScheme, x => ColorScheme = x, value); }
            remove { _binding.Remove("BorderColorScheme", value); }
        }

        public event BindingEventHandler<ColorBindingEventArgs> BindForeColor
        {
            add { _binding.Add("ForeColor", () => ForeColor, x => ForeColor = x, value); }
            remove { _binding.Remove("ForeColor", value); }
        }
        public void AddCustomBind<T>(Func<T> evalutateValue, Action<T> setPropertiesAcordingToValues)
        {
            _binding.CustomBind(evalutateValue, setPropertiesAcordingToValues);
        }


        public event BindingEventHandler<ColorBindingEventArgs> BindBackColor
        {
            add { _binding.Add("BackColor", () => BackColor, x => BackColor = x, value); }
            remove { _binding.Remove("BackColor", value); }
        }
        public event BindingEventHandler<ColorBindingEventArgs> BindBorderColor
        {
            add { _binding.Add("BorderColor", () => BorderColor, x => BorderColor = x, value); }
            remove { _binding.Remove("BorderColor", value); }
        }


        public event BindingEventHandler<ContextMenuStripBindingEventArgs> BindContextMenuStrip
        {
            add { _binding.Add("ContextMenuStrip", () => ContextMenuStrip, x => ContextMenuStrip = x, value); }
            remove { _binding.Remove("ContextMenuStrip", value); }
        }
        public event BindingEventHandler<ControlStyleBindingEventArgs> BindStyle
        {
            add { _binding.Add("Style", () => Style, x => Style = x, value); }
            remove { _binding.Remove("Style", value); }
        }


        internal virtual bool HandlesMouseFocusing(System.Windows.Forms.Control clickedControl, Point location)
        {
            return false;
        }

        internal Action<Action<Color>> _changeBackColor = delegate { };
        internal Action<Action<Color>> _changeForeColor = delegate { };

        internal void SetColorByGrid(GridRowPainting gridRowPainting)
        {
            _changeBackColor = delegate { };
            _changeForeColor = delegate { };
            gridRowPainting.AdjustBackColorForControls(_GetBackColor(), delegate (Color color) { _changeBackColor = delegate (Action<Color> obj) { obj(color); }; });

            gridRowPainting.AdjustForeColorForControls(
                delegate (Color color) { _changeForeColor = delegate (Action<Color> obj) { obj(color); }; });
            _ColorsChanged();
        }

        public void TryFocus()
        {
            if (!_AllowTryFocus()) return;
            _actionsForControls.Enqueue(
                () =>
                {
                    if (IsHandleCreated)
                    {
                        var cancel = false;
                        _InvokeUIPlatformCommand(
                            () =>
                            {
                                var c = (System.Windows.Forms.Control)this.FindForm();
                                while (c != null)
                                {
                                    if (!c.CanFocus)
                                    {
                                        cancel = true;
                                        return;
                                    }
                                    c = c.Parent;
                                }
                            });
                        if (cancel) return;
                    }
                    _TryFocus();
                });
        }

        internal virtual bool _AllowTryFocus() { return true; }

        internal virtual void _TryFocus()
        {
            _actionsForControls.RunActionWhichMayCauseAFocusChange(focusForm => focusForm(control => { }), this, true);
        }

        internal virtual bool HandleMouseDownAndReturnTrueToCancelMouseMessage(BringFocusToControl bringFocusToControl, Control clickedControl, Point location)
        {
            return false;
        }

        public override ContextMenuStrip ContextMenuStrip
        {
            get
            {
                if (_getDesignMode())
                    return base.ContextMenuStrip;
                ContextMenuStrip fcm = _virtualParent.GetContextMenuStrip(Parent);
                if (fcm == null)
                {
                    var form = this.FindForm() as Firefly.Box.UI.Form;
                    if (form != null)
                    {
                        fcm = form.ContextMenuStrip;
                        if (fcm == null)
                            fcm = form.GetContainerContextMenuStrip();
                    }
                }
                if (fcm != null && fcm is IContextMenu && ((IContextMenu)fcm).IsEmpty())
                    fcm = null;
                var r = base.ContextMenuStrip ?? fcm;



                return r;
            }
            set
            {
                base.ContextMenuStrip = value;
            }
        }

        Rectangle _startDragIfMouseMoveOusideThisArea = Rectangle.Empty;
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Clicks == 2)
                InternalOnMouseDoubleClick(e);
            else
                InternalOnMouseDown(e);

            base.OnMouseDown(e);
        }

        internal void _MouseDownForDrag(Point location)
        {
            var dragSize = SystemInformation.DragSize;
            _startDragIfMouseMoveOusideThisArea = new Rectangle(new Point(location.X - (dragSize.Width / 2), location.Y - (dragSize.Height / 2)), dragSize);
        }

        void NotifyMouseDownToParent(Action<Action> registerMouseUpAction)
        {
            if (!_mouseDownAfterChangingOfActiveGridRow)
                _virtualParent.NotifyMouseDown(registerMouseUpAction);
        }

        Action _mouseUpAction = () => { };
        internal void RegisterMouseUpAction(Action action)
        {
            _mouseUpAction = action;
        }

        internal void InternalOnMouseDown(MouseEventArgs e)
        {
            NotifyMouseDownToParent(RegisterMouseUpAction);
            InternalVirtualOnMouseDown(e);
        }
        internal virtual void InternalVirtualOnMouseDown(MouseEventArgs e)
        {
            HandleMouseEvent(e, (controlBase, args) => controlBase.InternalVirtualOnMouseDown(args));
            _MouseDownForDrag(new Point(e.X, e.Y));
        }

        internal void InternalOnMouseDoubleClick(MouseEventArgs e)
        {
            InternalVirtualOnMouseDoubleClick(e);
        }

        internal virtual void InternalVirtualOnMouseDoubleClick(MouseEventArgs e)
        {
            HandleMouseEvent(e, (controlBase, args) => controlBase.InternalOnMouseDoubleClick(args));
        }

        void HandleMouseEvent(MouseEventArgs e, Action<ControlBase, MouseEventArgs> action)
        {
            System.Windows.Forms.Control parent = this.Parent;
            if (parent != null)
            {
                Point parentPoint = e.Location;
                parentPoint.Offset(Location);
                for (var i = parent.Controls.GetChildIndex(this) + 1; i < parent.Controls.Count; i++)
                {
                    var controlBase = parent.Controls[i] as ControlBase;
                    if (controlBase != null && controlBase.Bounds.Contains(parentPoint) && controlBase.Visible && controlBase.Enabled)
                    {
                        var p = e.Location;
                        p.Offset(Location);
                        p.Offset(-controlBase.Left, -controlBase.Top);
                        action(controlBase, new MouseEventArgs(e.Button, e.Clicks, p.X, p.Y, e.Delta));
                        return;
                    }
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            InternalOnMouseUp(e);
            base.OnMouseUp(e);
        }

        internal void InternalOnMouseUp(MouseEventArgs e)
        {
            InternalVirtualOnMouseUp(e);
            _startDragIfMouseMoveOusideThisArea = Rectangle.Empty;
            InvokeRegisteredMouseUpAcion();
        }

        void InvokeRegisteredMouseUpAcion()
        {
            var x = _mouseUpAction;
            _mouseUpAction = () => { };
            x();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture)
            {
                _startDragIfMouseMoveOusideThisArea = Rectangle.Empty;
                InvokeRegisteredMouseUpAcion();
            }
        }

        internal virtual void InternalVirtualOnMouseUp(MouseEventArgs e)
        {
            HandleMouseEvent(e,
                (controlBase, args) => controlBase.InternalVirtualOnMouseUp(args));
        }

        protected override bool ProcessMnemonic(char charCode)
        {
            if (Control.ModifierKeys == Keys.Alt && ProcessMnemonicForTesting(charCode)) return true;
            return base.ProcessMnemonic(charCode);
        }

        internal virtual bool ProcessMnemonicForTesting(char c)
        {
            return false;
        }

        internal virtual ControlLayer _GetLayer()
        {
            return _controlBindingControl.SendBoundControlsToFrontOfZOrder() ?
                (ControlLayer)ContainerControlLayer.Instance : BackgroundControlLayer.Instance;
        }

        internal bool _IsAutoZOrder()
        {
            var f = FindForm() as Form;
            return f != null && f.AutoZOrder;
        }

        internal virtual void _ClickedOnGrid(Action doNotSendMouseMessage)
        {
        }

        internal void InternalOnDragDrop(DragEventArgs args)
        {
            OnDragDrop(args);
        }

        internal bool _IsHovered()
        {
            return _actionsForControls.IsHovered(this);
        }

        internal void _HoveredChanged()
        {
            if (_InvalidateOnHover())
                DoInvalidate();
        }

        internal virtual bool _InvalidateOnHover()
        {
            return false;
        }

        internal void _OnDragEnter(DragEventArgs drgevent)
        {
            OnDragEnter(drgevent);
        }
        public new int TabIndex
        {
            get { return base.TabIndex; }
            set { base.TabIndex = value; }
        }
        public bool ShouldSerializeTabIndex()
        {
            var f = FindForm() as Firefly.Box.UI.Form;
            if (f != null)
            {
                return f.TabOrderMode != TabOrderMode.Auto;
            }
            var p = Parent;
            while (p != null)
            {
                if (p is ReportSection)
                    return false;
                p = p.Parent;
            }
            return true;
        }

        public virtual void InputCharByCommand(char c)
        {

        }

        internal virtual bool _ClearBeforeControlClickCommandAfterLeavingRow()
        {
            return false;
        }

        internal virtual bool _IgnoreWhenPopulatingFlow()
        {
            return false;
        }

        bool _suspendAdvancedAnchor = false;
        internal void DoWhileAdvancedAnchorSuspended(Action action)
        {
            _suspendAdvancedAnchor = true;
            try
            {
                action();
            }
            finally
            {
                _suspendAdvancedAnchor = false;
            }
        }

        bool _moveWithoutResettingAdvancedAnchor;

        void DoWhileMovingWithoutResettingsAdvancedAnchor(Action action)
        {
            _moveWithoutResettingAdvancedAnchor = true;
            try
            {
                action();
            }
            finally
            {
                _moveWithoutResettingAdvancedAnchor = false;
            }
        }

        internal void _InvokeUIPlatformCommand(Action command)
        {
            _actionsForControls.DoOnUIThread(command);
        }

        internal T _InvokeUIPlatformFunc<T>(Func<T> x)
        {
            T result = default(T);
            _InvokeUIPlatformCommand(() => result = x());
            return result;
        }

        internal void _NotifyUIObserver(Action command)
        {
            _actionsForControls.DoOnNonUIThread(command);
        }

        internal virtual bool _AllowedToCauseExpandOfOtherControls()
        {
            return false;
        }

        bool IControl.AllowedToCauseExpandOfOtherControls()
        {
            return _AllowedToCauseExpandOfOtherControls();
        }

        internal virtual void RaiseClickCommand(Control clickedControl, Point location, Action<ControlBase> raise)
        {
            if (Enabled) raise(this);
        }

        internal void _OnDragDrop(DragEventArgs drgevent)
        {
            OnDragDrop(drgevent);
        }

        internal void _Drop(DragEventArgs drgevent, Action doBeforeDrop)
        {
            if (AllowDrop)
            {
                var dobj = new DataObject();

                var formats = new List<string>(new string[] { DataFormats.Text, DataFormats.OemText, DataFormats.Rtf, DataFormats.Html, DataFormats.SymbolicLink, DataFormats.FileDrop });
                if (DragDropFormats != null)
                    formats.AddRange(DragDropFormats);
                foreach (string format in formats)
                {
                    if (!string.IsNullOrEmpty(format) && drgevent.Data.GetDataPresent(format, true))
                        dobj.SetData(format, drgevent.Data.GetData(format));
                }

                var args = new DragEventArgs(dobj, drgevent.KeyState, drgevent.X, drgevent.Y, drgevent.AllowedEffect, drgevent.Effect);

                _RequestFocusAndDoWhenFormIsActive(doBeforeDrop);
                _actionsForControls.EnqueueAsFirst(
                    () =>
                    {
                        DragDropData = args.Data;
                        var z = DragDropEventArgs;
                        DragDropEventArgs = args;
                        var c = DragDropControl;
                        DragDropControl = this;
                        try
                        {
                            _DoDrop(args);
                        }
                        finally
                        {
                            DragDropEventArgs = z;
                            DragDropControl = c;
                        }
                    });
            }
        }

        internal virtual void _DoDrop(DragEventArgs args)
        {
        }

        internal bool _InvokeDragDropCommandAndReturnTrueIfHandled()
        {
            var handled = false;
            _actionsForControls.HandleCommand(Command.DragDrop, new object[0], this,
                handling => handling.OnCurrentlyActiveTask(invoking =>
                {
                    handled = true;
                    invoking.Invoke(() => { handled = false; });
                }));
            return handled;
        }

        internal void _SetDropData(DragEventArgs drgevent, Action andDo)
        {
            DragDropData = drgevent.Data;
            var z = DragDropEventArgs;
            DragDropEventArgs = drgevent;
            var c = DragDropControl;
            DragDropControl = this;
            try
            {
                andDo();
            }
            finally
            {
                DragDropData = null;
                DragDropEventArgs = z;
                DragDropControl = c;
            }
        }

        internal static bool IsReallyVisible(Control control)
        {
            ControlBase cb;
            return !control.IsHandleCreated && ((cb = control as ControlBase) == null || cb._visible.Value) || control.Visible || (cb = control as ControlBase) != null && cb.ForTestingForceVisible;
        }

        internal virtual bool IgnoreDoubleClick(Control controlClicked)
        {
            return false;
        }

        public int ZOrder { get; set; }

        protected virtual Size ToleratedContainerOverflow
        {
            get { return Size.Empty; }
        }

        internal Size ToleratedContainerOverflowInternal { get { return ToleratedContainerOverflow; } }

        internal virtual void _SetAutoTabIndex(Func<int> nextTabIndex, bool rightToLeft, bool setByBoundToControl)
        {
            rightToLeft = RightToLeft == RightToLeft.Yes || RightToLeft == RightToLeft.Inherit && rightToLeft;
            if (setByBoundToControl || _controlBindingControl.Control == null || !_controlBindingControl.Control._IsResponsibleForTabIndexOfAttachedControls())
            {
                TabIndex = nextTabIndex();
                _attachableControl.ForEachLayer(
                    controls =>
                    {
                        var containedControls = new List<System.Windows.Forms.Control>();
                        foreach (var ac in controls)
                        {
                            if (_IsResponsibleForTabIndexOfAttachedControls())
                                ac.VisitControl(c => containedControls.Add(c));
                        }
                        Form.SetTabOrderAcordingToPosition(containedControls, rightToLeft, true, nextTabIndex);
                    });
            }
            Form.SetTabOrderAcordingToPosition(Controls, rightToLeft, false, nextTabIndex);
        }

        internal virtual bool _IsResponsibleForTabIndexOfAttachedControls()
        {
            return false;
        }

        internal virtual void RaiseBeforeControlClick(Action<ControlBase> raise, Point location, bool isWithinActiveTask)
        {
            raise(isWithinActiveTask ? this : null);
        }

        internal virtual void RaiseSelectOnDoubleClick(Action raise)
        {
        }

        void PrintableControl.ResizeToFitInContainerWhilePrinting(int containerWidth, Action print)
        {
            if (!_boundsBindingCalculated || Width <= containerWidth - Left)
            {
                print();
                return;
            }
            var oldWidth = Width;
            Width = containerWidth - Left;
            try
            {
                print();
            }
            finally
            {
                Width = oldWidth;
            }
        }

        internal virtual bool AvoidEndEditingOfFocusedControlWhenRequestingFocus()
        {
            return false;
        }

        internal virtual bool _ScrollIntoViewWhenFocused()
        {
            return true;
        }

        bool _mouseDownAfterChangingOfActiveGridRow;
        internal void SendMouseDownAfterChangingOfActiveGridRow(Action sendMouseDown)
        {
            _mouseDownAfterChangingOfActiveGridRow = true;
            try
            {
                sendMouseDown();
            }
            finally
            {
                _mouseDownAfterChangingOfActiveGridRow = false;
            }
        }

        internal virtual bool HandlesMouseDoubleClickFocusing()
        {
            return false;
        }

        internal virtual bool _IgnoreBoundControlsWhenCalculatingContainerDisplaySize()
        {
            return false;
        }

        internal virtual ControlBase GetInnerControlBase(Control clickedControl, Point location)
        {
            return this;
        }

        internal virtual int _GetTopPaddingForPaintingArea()
        {
            return 0;
        }

        internal virtual int _GetLeftPaddingForPaintingArea()
        {
            return 0;
        }

        internal ControlExtender _controlExtender;

        class myControlExtenderClient : ControlExtender.Client
        {
            ControlBase _parent;

            public myControlExtenderClient(ControlBase parent)
            {
                _parent = parent;
            }

            public int Width
            {
                get { return _parent.DeferredWidth; }
                set { _parent.DeferredWidth = value; }
            }

            public int Left
            {
                get { return _parent.DeferredLeft; }
                set { _parent.DeferredLeft = value; }
            }

            public int Top
            {
                get { return _parent.DeferredTop; }
                set
                {
                    _parent._topBound = true;
                    _parent.DeferredTop = value;
                }
            }

            public int Height
            {
                get { return _parent.DeferredHeight; }
                set { _parent.DeferredHeight = value; }
            }

            public bool ForceAbsoluteBindLeftWhenLeftAdvancedAnchorIsZero()
            {
                return _parent._rightToLeftForm && !_parent.ForceRelativeBindLeftWhenRightToLeftYes;
            }

            public void SetWidth(int width, int clippedWidth, Action<int> setAction)
            {
                _parent._clippedWidth = clippedWidth;
                _parent._WidthChangedDueToBindWidth(width, setAction);
            }

            public void BoundsBindingCalculated()
            {
                _parent._boundsBindingCalculated = true;
            }
        }

        public event BindingEventHandler<IntBindingEventArgs> BindLeft
        {
            add { _controlExtender.BindLeft += value; }
            remove { _controlExtender.BindLeft -= value; }
        }

        public event BindingEventHandler<IntBindingEventArgs> BindTop
        {
            add { _controlExtender.BindTop += value; }
            remove { _controlExtender.BindTop -= value; }
        }

        public event BindingEventHandler<IntBindingEventArgs> BindWidth
        {
            add { _controlExtender.BindWidth += value; }
            remove { _controlExtender.BindWidth -= value; }
        }

        internal bool _isHeightBound;
        public event BindingEventHandler<IntBindingEventArgs> BindHeight
        {
            add
            {
                _controlExtender.BindHeight += value;
                _isHeightBound = true;
            }
            remove { _controlExtender.BindHeight -= value; }
        }

        event DisplaySizeChanged _displaySizeChanged;
        event DisplaySizeChanged HasDisplaySize.DisplaySizeChanged
        {
            add { _displaySizeChanged += value; }
            remove { _displaySizeChanged -= value; }
        }

        public bool AbsoluteBindTop { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Browsable(false)]

        public bool ForceRelativeBindLeftWhenRightToLeftYes { get; set; }

        bool PrintableControl.CanBeExpandedDuringPrinting
        {
            get
            {
                return _canBeExpandedDuringPrinting();


            }
        }

        internal void UpdateBounds(Rectangle bounds)
        {
            UpdateBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        Size HasDisplaySize.DisplaySize
        {
            get
            {
                return ClientSize;
            }
        }

        Size HasDisplaySize.OriginalDisplaySize
        {
            get
            {
                return _controlExtender.GetOriginalSize();
            }
        }

        Size HasDisplaySize.ClientSize
        {
            get
            {
                return ClientSize;
            }
        }

        Size HasDisplaySize.OriginalClientSize
        {
            get
            {
                return _controlExtender.GetOriginalSize();
            }
        }

        object IControl.Tag
        {
            get
            {
                return Tag;
            }
        }

        internal virtual bool _canBeExpandedDuringPrinting()
        {
            return false;
        }


        bool _topBound;

        internal bool _TreatTopAsAbsoluteInGridPainting()
        {
            return AbsoluteBindTop && _IsTopBound();
        }

        protected virtual bool _IsTopBound()
        {
            return _topBound;
        }

        ControlBase PrintableControl.GetControlBase()
        {
            return this;
        }

        internal virtual bool _IsPseudoContainer()
        {
            return false;
        }

        void IControl.ReevaluateTabIndex()
        {
            _tabIndexBinding.Apply();
        }
        internal int GetDeferredLeftAbsoluteInGrid()
        {
            return _virtualParent.ToReal(DeferredLeft);
        }

        void AttachedControl.VisitControl(Action<Control> visitor)
        {
            visitor(this);
        }

        bool PrintableControl.BoundsIntersectWith(Rectangle bounds)
        {
            return Bounds.IntersectsWith(bounds);
        }

        internal virtual bool BlockMouseDown(Control controlClicked, Point location)
        {
            return false;
        }

        bool IControl.AreYouThis(IControl checkMe)
        {
            return checkMe.AreYou(this);
        }

        bool IControl.Matches(Predicate<ControlBase> ifControlMatches)
        {
            return ifControlMatches(this);
        }

        Point HasDisplaySize.Location
        {
            get
            {
                return _GetLocationForAdvancedAnchorOfAttachedControls();
            }
        }

        internal virtual Point _GetLocationForAdvancedAnchorOfAttachedControls()
        {
            return new Point(DeferredLeft, DeferredTop);
        }
    }



    public class ControlExtender : Component, PropertiesForWinFormsControlForFlowWrapper, AttachedControl
    {
        [DefaultValue(0)]
        public int ZOrder { get; set; }
        internal interface Client
        {
            int Width { get; set; }
            int Left { get; set; }
            int Height { get; set; }
            int Top { get; set; }
            void SetWidth(int width, int clippedWidth, Action<int> setAction);
            void BoundsBindingCalculated();
            bool ForceAbsoluteBindLeftWhenLeftAdvancedAnchorIsZero();
        }

        class myClient : Client
        {
            ControlExtender _parent;

            public myClient(ControlExtender parent)
            {
                _parent = parent;
            }

            public int Width
            {
                get { return _parent._control.Width; }
                set { _parent._actionsForControls.DoOnUIThread(() => _parent._control.Width = value); }
            }

            public int Left
            {
                get { return _parent._control.Left; }
                set { _parent._actionsForControls.DoOnUIThread(() => _parent._control.Left = value); }
            }

            public int Top
            {
                get { return _parent._control.Top; }
                set { _parent._actionsForControls.DoOnUIThread(() => _parent._control.Top = value); }
            }

            public int Height
            {
                get { return _parent._control.Height; }
                set { _parent._actionsForControls.DoOnUIThread(() => _parent._control.Height = value); }
            }

            public bool ForceAbsoluteBindLeftWhenLeftAdvancedAnchorIsZero()
            {
                return false;
            }

            public void SetWidth(int width, int clippedWidth, Action<int> setAction)
            {
                setAction(width);
            }

            public void BoundsBindingCalculated()
            {
            }
        }

        Client _client;
        Control _control;
        Binding _binding;
        ActionsForControls _actionsForControls = new NullActionsForControls();

        public ControlExtender()
        {
            _binding = new Binding(this);
            _client = new myClient(this);
        }

        internal ControlExtender(Client client, Control control)
        {
            _client = client;
            _control = control;
            _binding = new Binding(_control);

        }

        internal void Load(PropertyBinder binder, ActionsForControls actionsForControls)
        {
            _actionsForControls = actionsForControls;
            _BindProperties(binder);
            ApplySpecialAnchor(new myControlForAdvancedAnchor(Control), manager => manager.ApplySpecialAnchor(Control.Parent, false, false));
            Control.LocationChanged += (sender, args) => BoundsChanged(false);
            Control.SizeChanged += (sender, args) => BoundsChanged(false);
        }

        internal void _BindProperties(PropertyBinder binder)
        {
            _binding.Bind(binder);
        }

        class myControlForAdvancedAnchor : ControlForSpecialAnchorManager
        {
            Control _parent;

            public myControlForAdvancedAnchor(Control parent)
            {
                _parent = parent;
            }

            public Control Parent
            {
                get { return _parent.Parent; }
            }

            public void SetBounds(int left, int top, int width, int height, int clippedWidth)
            {
                _parent.SetBounds(left, top, width, height);
            }

            public void SetAnchor(AnchorStyles anchorStyles)
            {
                _parent.Anchor = anchorStyles;
            }

            public Rectangle GetCurrentBounds()
            {
                return _parent.Bounds;
            }
        }

        bool _allowFocus = true;
        [BehaviorCategory]
        [DefaultValue(true)]
        public bool AllowFocus
        {
            get { return _allowFocus; }
            set { _allowFocus = value; }
        }

        [BehaviorCategory, DefaultValue(false)]
        public bool AllowFocusWhenNoRows { get; set; }

        bool _enabled = true;
        [BehaviorCategory]
        [DefaultValue(true)]
        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                _enabled = value;
                if (Control != null)
                    _actionsForControls.DoOnUIThread(() => Control.Enabled = value);
            }
        }

        public event BindingEventHandler<BooleanBindingEventArgs> BindEnabled
        {
            add { _binding.Add("Enabled", () => Enabled, x => Enabled = x, value); }
            remove { _binding.Remove("Enabled", value); }
        }

        bool _visible = true;
        [AppearanceCategory]
        [DefaultValue(true)]
        public bool Visible
        {
            get { return _visible; }
            set
            {
                _visible = value;
                ApplyVisible();
            }
        }

        AdvancedAnchor _advancedAnchor = new AdvancedAnchor(0, 0, 0, 0, false);
        [LayoutCategory]
        public AdvancedAnchor AdvancedAnchor
        {
            get { return _advancedAnchor; }
            set { _advancedAnchor = value; }
        }

        public bool ShouldSerializeAdvancedAnchor()
        {
            return !new AdvancedAnchor(0, 0, 0, 0, false).Equals(_advancedAnchor);
        }

        [DefaultValue(null)]
        public System.Windows.Forms.Control Control
        {
            get { return _control; }
            set
            {
                if (_control != null && _controlBindingControl != ControlBinding.Default)
                    _controlBindingControl.Detach(this);
                _control = value;
                if (_controlBindingControl != ControlBinding.Default)
                {
                    _controlBindingControl.Attach(this);
                    if (_control.Parent != null)
                        _control.Parent.PerformLayout(_control, UI.ZOrder.CausesZOrderChange);
                }
                TryAddingToContainer();
            }
        }

        public event BindingEventHandler<BooleanBindingEventArgs> BindVisible
        {
            add { _binding.Add("Visible", () => Visible, x => Visible = x, value); }
            remove { _binding.Remove("Visible", value); }
        }

        public event BindingEventHandler<BooleanBindingEventArgs> BindAllowFocus;

        internal bool GetAllowFocus(bool noData)
        {
            if (noData && !AllowFocusWhenNoRows) return false;
            if (BindAllowFocus == null) return AllowFocus;
            var x = new BooleanBindingEventArgs(AllowFocus);
            BindAllowFocus(_control, x);
            AllowFocus = x.Value;
            return AllowFocus;
        }

        bool PropertiesForWinFormsControlForFlowWrapper.AllowFocus()
        {
            return GetAllowFocus(false) && Enabled && Visible && _visibleDueToBoundToControl;
        }

        public event Action BindProperties
        {
            add { _binding.Add("Properties", () => (int)0, i => { }, (sender, args) => value()); }
            remove { throw new NotImplementedException(); }
        }

        SpecialAnchorManager _specialAnchorManager = new SpecialAnchorManagerDummy();
        ContainerControl _containerControl;

        internal bool LocationAnchored()
        {
            return _advancedAnchor.LeftPercentage != 0 || _advancedAnchor.RightPercentage != 0;
        }

        internal void BoundsChanged(bool moveWithoutResettings)
        {
            _specialAnchorManager.BoundsChanged(moveWithoutResettings);
        }

        internal void DoDispose()
        {
            _specialAnchorManager.Dispose();
        }

        Rectangle _originalBounds;
        internal void ApplySpecialAnchor(ControlForSpecialAnchorManager controlForSpecialAnchor, Action<SpecialAnchorManager> apply)
        {
            _specialAnchorManager = new SpecialAnchorManagerClass(controlForSpecialAnchor, _advancedAnchor);
            _controlBindingControl.ApplySpecialAnchor(_specialAnchorManager, _control.Bounds, () => apply(_specialAnchorManager));
            _originalBounds = _control.Bounds;
        }

        internal bool UseRelativeDeferredLeftInRightToLeft()
        {
            return _advancedAnchor.UseRelativeDeferredLeftInRightToLeft();
        }

        public ContainerControl ContainerControl
        {
            get { return _containerControl; }
            set
            {
                _containerControl = value;
                TryAddingToContainer();
            }
        }

        void TryAddingToContainer()
        {
            if (Control == null || ContainerControl == null) return;
            var f = ContainerControl as Form;
            if (f != null)
                f.AddControlExtensions(Control, this);
        }

        public override ISite Site
        {
            set
            {
                base.Site = value;
                if (value == null)
                    return;
                var designerHost = value.GetService(typeof(IDesignerHost)) as IDesignerHost;
                if (designerHost == null)
                    return;
                var rootComponent = designerHost.RootComponent;
                if (!(rootComponent is ContainerControl))
                    return;
                this.ContainerControl = (ContainerControl)rootComponent;
            }
        }

        public event BindingEventHandler<IntBindingEventArgs> BindWidth
        {
            add
            {
                _binding.Add("Width", () => _client.Width, SetFromBindWidth, value);
            }
            remove { _binding.Remove("Width", value); }
        }

        void SetFromBindWidth(int width)
        {
            _client.BoundsBindingCalculated();
            _specialAnchorManager.AdjustWidth(width < 0 ? 0 : width,
                (w, clippedWidth) => _client.SetWidth(w, clippedWidth, y => _client.Width = y));
        }

        public event BindingEventHandler<IntBindingEventArgs> BindLeft
        {
            add
            {
                _binding.Add("Left", () => _client.Left, SetFromBindLeft, value);
            }
            remove { _binding.Remove("Left", value); }
        }

        void SetFromBindLeft(int left)
        {
            _client.BoundsBindingCalculated();
            if (AdvancedAnchor.Enabled && AdvancedAnchor.LeftPercentage == 0 && _client.ForceAbsoluteBindLeftWhenLeftAdvancedAnchorIsZero())
                _client.Left = left + GetAutoScrollPosition().X;
            else
                _specialAnchorManager.AdjustLeft(left, y => _client.Left = y + GetAutoScrollPosition().X);
        }

        public Point GetAutoScrollPosition()
        {
            var p = _control.Parent as ScrollableControl;
            if (p != null) return p.AutoScrollPosition;
            return Point.Empty;
        }

        public event BindingEventHandler<IntBindingEventArgs> BindHeight
        {
            add
            {
                _binding.Add("Height", () => _client.Height, SetFromBindHeight, value);
            }
            remove { _binding.Remove("Height", value); }
        }

        void SetFromBindHeight(int height)
        {
            _client.BoundsBindingCalculated();
            _specialAnchorManager.AdjustHeight(height < 0 ? 0 : height, y => _client.Height = y);
        }

        public event BindingEventHandler<IntBindingEventArgs> BindTop
        {
            add
            {
                _binding.Add("Top", () => _client.Top, SetFromBindTop, value);
            }
            remove { _binding.Remove("Top", value); }
        }

        void SetFromBindTop(int top)
        {
            _client.BoundsBindingCalculated();
            _specialAnchorManager.AdjustTop(top, y => _client.Top = y + GetAutoScrollPosition().Y);
        }

        internal bool IsWidthBound()
        {
            return _binding.HasIntEvent("Width");
        }

        public new virtual event Action Enter { add { } remove { } }
        public new virtual event Action Leave { add { } remove { } }
        public virtual event Action InputValidation { add { } remove { } }

        internal void ApplyBinding()
        {
            _binding.Apply();
        }

        internal void ResetBinding()
        {
            _binding.Reset();
        }

        bool _visibleDueToBoundToControl = true;
        internal void SetVisibleDueToBoundControl(bool value)
        {
            _visibleDueToBoundToControl = value;
            ApplyVisible();
        }

        void ApplyVisible()
        {
            var v = _visible && _visibleDueToBoundToControl;
            if (Control != null)
                _actionsForControls.DoOnUIThread(() => Control.Visible = v);
        }

        internal Size GetOriginalSize()
        {
            return _specialAnchorManager.GetOriginalSize(Control.ClientSize);
        }

        void AttachedControl.SetVisible(bool value)
        {
            SetVisibleDueToBoundControl(value);
        }

        void AttachedControl.SetEnabled(bool value)
        {
            _control.Enabled = value;
        }

        void AttachedControl.MoveLeft(int delta)
        {
            _control.Left += delta;
        }

        void AttachedControl.SetTabIndex(int tabIndex)
        {
            _control.TabIndex = tabIndex;
        }

        void AttachedControl.MoveTop(int delta)
        {
            _control.Top += delta;
        }

        bool AttachedControl.BoundsIntersectWith(Rectangle bounds)
        {
            return bounds.Contains(_originalBounds) || _originalBounds.IntersectsWith(bounds);
        }

        void AttachedControl.VisitControl(Action<Control> visitor)
        {
            visitor(_control);
        }

        ControlBinding _controlBindingControl = ControlBinding.Default;
        /// <summary>
        /// Determines to which control this control is bound to.
        /// </summary>
        public ControlBinding BoundTo
        {
            get
            {
                return _controlBindingControl;
            }
            set
            {
                if (Control != null)
                    _controlBindingControl.Detach(this);
                _controlBindingControl = value ?? ControlBinding.Default;
                if (Control != null)
                {
                    _controlBindingControl.Attach(this);
                    if (_control.Parent != null)
                        _control.Parent.PerformLayout(_control, UI.ZOrder.CausesZOrderChange);
                }
            }
        }

        public bool ShouldSerializeBoundTo()
        {
            return _controlBindingControl != ControlBinding.Default;
        }

        public void ResetBoundTo()
        {
            _controlBindingControl = ControlBinding.Default;
        }

        internal void SetFromBindBounds(int x, int y, int width, int height)
        {
            if (x != _client.Left)
                SetFromBindLeft(x);
            if (y != _client.Top)
                SetFromBindTop(y);
            if (width != _client.Width)
                SetFromBindWidth(width);
            if (height != _client.Height)
                SetFromBindHeight(height);
        }


        internal void Moved()
        {
            _specialAnchorManager.ControlMoved();
        }

        internal Rectangle AdjustBounds(Rectangle rectangle)
        {
            return _specialAnchorManager.AdjustBounds(rectangle);
        }
    }

    public interface IContextMenu
    {
        bool IsEmpty();
    }
}
