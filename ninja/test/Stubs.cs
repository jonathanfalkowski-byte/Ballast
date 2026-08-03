// ─────────────────────────────────────────────────────────────────────────────
// Ballast — test/Stubs.cs        COMPILE-CHECK SCAFFOLDING. NOT PRODUCT CODE.
//
// WHAT THIS IS
//   Hand-written, do-nothing declarations of the WPF and NinjaTrader types that
//   Ballast's UI files reference. The development sandbox has the Mono C#
//   compiler (mcs) but neither PresentationFramework/PresentationCore nor any
//   NinjaTrader assembly, so without these the UI files cannot even be parsed
//   for type errors.
//
//   With this file on the command line the whole add-on type-checks offline:
//
//       mcs -target:library -out:/tmp/ui.dll test/Stubs.cs Ballast/*.cs
//
//   That catches typos, wrong signatures, bad casts and dead references in
//   minutes instead of after a round trip through NinjaTrader's F5 compile.
//
// WHAT IT IS NOT
//   It is NOT a WPF implementation. Every method body is empty and every
//   property is an auto-property. Nothing here is ever executed. The signatures
//   and the inheritance chain are the only things that matter, and they mirror
//   the real ones closely enough that the product source compiles unchanged.
//
// ── NEVER COPY THIS FILE INTO NinjaTrader ───────────────────────────────────
//   Do NOT put Stubs.cs into Documents\NinjaTrader 8\bin\Custom\ (or anywhere
//   underneath it). NinjaTrader compiles every .cs file under bin\Custom into
//   one assembly; these stubs would collide with the real WPF and NinjaTrader
//   types and take the entire NinjaScript build — every indicator and strategy
//   the trader owns — down with them.
//
//   Ship only the Ballast folder. This file stays in the repo's test/ directory
//   and is never distributed.
//
// RULES FOR EDITING
//   * Never edit anything under Ballast/ to make this file happy. This file
//     exists to serve the product, not the other way round.
//   * No #if / #define. It is a separate file that simply never ships.
//   * Do not declare System.Windows.Input.ICommand — Mono's System.dll already
//     defines it and a second definition produces CS0436.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Threading
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Threading
{
    public enum DispatcherPriority
    {
        Invalid = -1, Inactive = 0, SystemIdle = 1, ApplicationIdle = 2, ContextIdle = 3,
        Background = 4, Input = 5, Loaded = 6, Render = 7, DataBind = 8, Normal = 9, Send = 10
    }

    public class DispatcherOperation
    {
        public object Result { get { return null; } }
        public DispatcherOperationStatus Status { get { return DispatcherOperationStatus.Completed; } }
        public DispatcherOperationStatus Wait() { return DispatcherOperationStatus.Completed; }
        public DispatcherOperationStatus Wait(TimeSpan timeout) { return DispatcherOperationStatus.Completed; }
        public bool Abort() { return false; }
    }

    public enum DispatcherOperationStatus { Pending, Aborted, Completed, Executing }

    public sealed class Dispatcher
    {
        public static Dispatcher CurrentDispatcher { get { return null; } }
        public System.Threading.Thread Thread { get { return null; } }
        public bool HasShutdownStarted { get { return false; } }

        public bool CheckAccess() { return true; }
        public void VerifyAccess() { }

        public object Invoke(Delegate method, params object[] args) { return null; }
        public object Invoke(Delegate method, DispatcherPriority priority, params object[] args) { return null; }
        public void Invoke(Action callback) { }
        public void Invoke(Action callback, DispatcherPriority priority) { }
        public TResult Invoke<TResult>(Func<TResult> callback) { return default(TResult); }

        public DispatcherOperation BeginInvoke(Delegate method, params object[] args) { return null; }
        public DispatcherOperation BeginInvoke(Delegate method, DispatcherPriority priority, params object[] args) { return null; }
        public DispatcherOperation BeginInvoke(Action callback) { return null; }
        public DispatcherOperation BeginInvoke(Action callback, DispatcherPriority priority) { return null; }

        public DispatcherOperation InvokeAsync(Action callback) { return null; }
        public DispatcherOperation InvokeAsync(Action callback, DispatcherPriority priority) { return null; }
        public DispatcherOperation InvokeAsync(Delegate method) { return null; }

        public void InvokeShutdown() { }
        public static void Run() { }
    }

    public abstract class DispatcherObject
    {
        public Dispatcher Dispatcher { get { return null; } }
        public bool CheckAccess() { return true; }
        public void VerifyAccess() { }
    }

    public class DispatcherTimer
    {
        public DispatcherTimer() { }
        public DispatcherTimer(DispatcherPriority priority) { }
        public DispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback, Dispatcher dispatcher) { }

        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; set; }
        public object Tag { get; set; }
        public Dispatcher Dispatcher { get { return null; } }

        public event EventHandler Tick;

        public void Start() { if (Tick != null) { } }
        public void Stop() { }
    }

    public class DispatcherUnhandledExceptionEventArgs : EventArgs
    {
        public Exception Exception { get { return null; } }
        public bool Handled { get; set; }
    }

    public delegate void DispatcherUnhandledExceptionEventHandler(object sender, DispatcherUnhandledExceptionEventArgs e);
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows  — core dependency/element layer, layout primitives, enums
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows
{
    using System.Windows.Media;
    using System.Windows.Threading;

    // ── Dependency layer ─────────────────────────────────────────────────────

    public class DependencyProperty
    {
        public string Name { get { return null; } }
        public Type PropertyType { get { return null; } }
        public Type OwnerType { get { return null; } }

        public static DependencyProperty Register(string name, Type propertyType, Type ownerType) { return null; }
        public static DependencyProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata typeMetadata) { return null; }
        public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType) { return null; }
        public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata defaultMetadata) { return null; }
        public DependencyPropertyKey RegisterReadOnly(string name, Type propertyType, Type ownerType, PropertyMetadata typeMetadata) { return null; }
        public DependencyProperty AddOwner(Type ownerType) { return null; }
    }

    public sealed class DependencyPropertyKey
    {
        public DependencyProperty DependencyProperty { get { return null; } }
    }

    public class PropertyMetadata
    {
        public PropertyMetadata() { }
        public PropertyMetadata(object defaultValue) { }
        public PropertyMetadata(PropertyChangedCallback propertyChangedCallback) { }
        public PropertyMetadata(object defaultValue, PropertyChangedCallback propertyChangedCallback) { }
        public object DefaultValue { get; set; }
    }

    public class FrameworkPropertyMetadata : PropertyMetadata
    {
        public FrameworkPropertyMetadata() { }
        public FrameworkPropertyMetadata(object defaultValue) : base(defaultValue) { }
        public FrameworkPropertyMetadata(object defaultValue, PropertyChangedCallback cb) : base(defaultValue, cb) { }
        public FrameworkPropertyMetadata(object defaultValue, FrameworkPropertyMetadataOptions flags) : base(defaultValue) { }
        public FrameworkPropertyMetadata(object defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback cb) : base(defaultValue, cb) { }
    }

    [Flags]
    public enum FrameworkPropertyMetadataOptions
    {
        None = 0, AffectsMeasure = 1, AffectsArrange = 2, AffectsParentMeasure = 4,
        AffectsParentArrange = 8, AffectsRender = 16, Inherits = 32,
        BindsTwoWayByDefault = 256, Journal = 1024
    }

    public struct DependencyPropertyChangedEventArgs
    {
        public DependencyProperty Property { get { return null; } }
        public object OldValue { get { return null; } }
        public object NewValue { get { return null; } }
    }

    public delegate void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e);
    public delegate void DependencyPropertyChangedEventHandler(object sender, DependencyPropertyChangedEventArgs e);

    public class DependencyObject : DispatcherObject
    {
        public object GetValue(DependencyProperty dp) { return null; }
        public void SetValue(DependencyProperty dp, object value) { }
        public void SetValue(DependencyPropertyKey key, object value) { }
        public void ClearValue(DependencyProperty dp) { }
        public void CoerceValue(DependencyProperty dp) { }
        public object ReadLocalValue(DependencyProperty dp) { return null; }
        public bool IsSealed { get { return false; } }
        public Type DependencyObjectType { get { return null; } }
        protected virtual void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { }
    }

    public class Freezable : DependencyObject
    {
        public bool CanFreeze { get { return true; } }
        public bool IsFrozen { get { return false; } }
        public void Freeze() { }
        public Freezable Clone() { return null; }
        public Freezable GetAsFrozen() { return null; }
        public Freezable GetCurrentValueAsFrozen() { return null; }
    }

    // ── Routed events ────────────────────────────────────────────────────────

    public class RoutedEvent
    {
        public string Name { get { return null; } }
        public Type HandlerType { get { return null; } }
        public Type OwnerType { get { return null; } }
    }

    public enum RoutingStrategy { Tunnel, Bubble, Direct }

    public class RoutedEventArgs : EventArgs
    {
        public RoutedEventArgs() { }
        public RoutedEventArgs(RoutedEvent routedEvent) { }
        public RoutedEventArgs(RoutedEvent routedEvent, object source) { }

        public bool Handled { get; set; }
        public object OriginalSource { get; set; }
        public object Source { get; set; }
        public RoutedEvent RoutedEvent { get; set; }
    }

    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);

    public class EventManager
    {
        public static RoutedEvent RegisterRoutedEvent(string name, RoutingStrategy routingStrategy, Type handlerType, Type ownerType) { return null; }
    }

    // ── Structs ──────────────────────────────────────────────────────────────

    public struct Point
    {
        public Point(double x, double y) { this.x = x; this.y = y; }
        private double x, y;
        public double X { get { return x; } set { x = value; } }
        public double Y { get { return y; } set { y = value; } }
        public override string ToString() { return ""; }
        public static bool operator ==(Point a, Point b) { return false; }
        public static bool operator !=(Point a, Point b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Size
    {
        public Size(double width, double height) { w = width; h = height; }
        private double w, h;
        public double Width { get { return w; } set { w = value; } }
        public double Height { get { return h; } set { h = value; } }
        public bool IsEmpty { get { return false; } }
        public static Size Empty { get { return new Size(); } }
        public static bool operator ==(Size a, Size b) { return false; }
        public static bool operator !=(Size a, Size b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Rect
    {
        public Rect(double x, double y, double width, double height) { _x = x; _y = y; _w = width; _h = height; }
        public Rect(Point location, Size size) { _x = location.X; _y = location.Y; _w = size.Width; _h = size.Height; }
        public Rect(Point p1, Point p2) { _x = p1.X; _y = p1.Y; _w = p2.X; _h = p2.Y; }
        public Rect(Size size) { _x = 0; _y = 0; _w = size.Width; _h = size.Height; }
        private double _x, _y, _w, _h;
        public double X { get { return _x; } set { _x = value; } }
        public double Y { get { return _y; } set { _y = value; } }
        public double Width { get { return _w; } set { _w = value; } }
        public double Height { get { return _h; } set { _h = value; } }
        public double Left { get { return _x; } }
        public double Top { get { return _y; } }
        public double Right { get { return _x + _w; } }
        public double Bottom { get { return _y + _h; } }
        public bool IsEmpty { get { return false; } }
        public static Rect Empty { get { return new Rect(); } }
        public bool Contains(Point p) { return false; }
        public static bool operator ==(Rect a, Rect b) { return false; }
        public static bool operator !=(Rect a, Rect b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Int32Rect
    {
        public Int32Rect(int x, int y, int width, int height) { _x = x; _y = y; _w = width; _h = height; }
        private int _x, _y, _w, _h;
        public int X { get { return _x; } set { _x = value; } }
        public int Y { get { return _y; } set { _y = value; } }
        public int Width { get { return _w; } set { _w = value; } }
        public int Height { get { return _h; } set { _h = value; } }
        public bool HasArea { get { return false; } }
        public static Int32Rect Empty { get { return new Int32Rect(); } }
        public static bool operator ==(Int32Rect a, Int32Rect b) { return false; }
        public static bool operator !=(Int32Rect a, Int32Rect b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Vector
    {
        public Vector(double x, double y) { _x = x; _y = y; }
        private double _x, _y;
        public double X { get { return _x; } set { _x = value; } }
        public double Y { get { return _y; } set { _y = value; } }
        public double Length { get { return 0; } }
        public static bool operator ==(Vector a, Vector b) { return false; }
        public static bool operator !=(Vector a, Vector b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Thickness
    {
        public Thickness(double uniformLength) { l = t = r = b = uniformLength; }
        public Thickness(double left, double top, double right, double bottom) { l = left; t = top; r = right; b = bottom; }
        private double l, t, r, b;
        public double Left { get { return l; } set { l = value; } }
        public double Top { get { return t; } set { t = value; } }
        public double Right { get { return r; } set { r = value; } }
        public double Bottom { get { return b; } set { b = value; } }
        public static bool operator ==(Thickness a, Thickness b) { return false; }
        public static bool operator !=(Thickness a, Thickness b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct CornerRadius
    {
        public CornerRadius(double uniformRadius) { tl = tr = br = bl = uniformRadius; }
        public CornerRadius(double topLeft, double topRight, double bottomRight, double bottomLeft)
        { tl = topLeft; tr = topRight; br = bottomRight; bl = bottomLeft; }
        private double tl, tr, br, bl;
        public double TopLeft { get { return tl; } set { tl = value; } }
        public double TopRight { get { return tr; } set { tr = value; } }
        public double BottomRight { get { return br; } set { br = value; } }
        public double BottomLeft { get { return bl; } set { bl = value; } }
        public static bool operator ==(CornerRadius a, CornerRadius b) { return false; }
        public static bool operator !=(CornerRadius a, CornerRadius b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public enum GridUnitType { Auto, Pixel, Star }

    public struct GridLength
    {
        public GridLength(double pixels) { v = pixels; t = GridUnitType.Pixel; }
        public GridLength(double value, GridUnitType type) { v = value; t = type; }
        private double v; private GridUnitType t;
        public double Value { get { return v; } }
        public GridUnitType GridUnitType { get { return t; } }
        public bool IsAbsolute { get { return t == GridUnitType.Pixel; } }
        public bool IsAuto { get { return t == GridUnitType.Auto; } }
        public bool IsStar { get { return t == GridUnitType.Star; } }
        public static GridLength Auto { get { return new GridLength(1, GridUnitType.Auto); } }
        public static bool operator ==(GridLength a, GridLength b) { return false; }
        public static bool operator !=(GridLength a, GridLength b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    // ── Layout / text enums ──────────────────────────────────────────────────

    public enum Visibility { Visible, Hidden, Collapsed }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public enum TextAlignment { Left, Right, Center, Justify }
    public enum TextWrapping { WrapWithOverflow, NoWrap, Wrap }
    public enum TextTrimming { None, CharacterEllipsis, WordEllipsis }
    public enum FlowDirection { LeftToRight, RightToLeft }
    public enum LineStackingStrategy { MaxHeight, BlockLineHeight }
    public enum BaselineAlignment { Top, Center, Bottom, Baseline, TextTop, TextBottom, Subscript, Superscript }
    public enum SizeToContent { Manual, Width, Height, WidthAndHeight }
    public enum WindowState { Normal, Minimized, Maximized }
    public enum WindowStyle { None, SingleBorderWindow, ThreeDBorderWindow, ToolWindow }
    public enum WindowStartupLocation { Manual, CenterScreen, CenterOwner }
    public enum ResizeMode { NoResize, CanMinimize, CanResize, CanResizeWithGrip }

    public struct FontWeight
    {
        public override string ToString() { return ""; }
        public static bool operator ==(FontWeight a, FontWeight b) { return false; }
        public static bool operator !=(FontWeight a, FontWeight b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public static class FontWeights
    {
        public static FontWeight Thin { get { return new FontWeight(); } }
        public static FontWeight ExtraLight { get { return new FontWeight(); } }
        public static FontWeight UltraLight { get { return new FontWeight(); } }
        public static FontWeight Light { get { return new FontWeight(); } }
        public static FontWeight Normal { get { return new FontWeight(); } }
        public static FontWeight Regular { get { return new FontWeight(); } }
        public static FontWeight Medium { get { return new FontWeight(); } }
        public static FontWeight DemiBold { get { return new FontWeight(); } }
        public static FontWeight SemiBold { get { return new FontWeight(); } }
        public static FontWeight Bold { get { return new FontWeight(); } }
        public static FontWeight ExtraBold { get { return new FontWeight(); } }
        public static FontWeight UltraBold { get { return new FontWeight(); } }
        public static FontWeight Black { get { return new FontWeight(); } }
        public static FontWeight Heavy { get { return new FontWeight(); } }
        public static FontWeight ExtraBlack { get { return new FontWeight(); } }
        public static FontWeight UltraBlack { get { return new FontWeight(); } }
    }

    public struct FontStyle
    {
        public override string ToString() { return ""; }
        public static bool operator ==(FontStyle a, FontStyle b) { return false; }
        public static bool operator !=(FontStyle a, FontStyle b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public static class FontStyles
    {
        public static FontStyle Normal { get { return new FontStyle(); } }
        public static FontStyle Italic { get { return new FontStyle(); } }
        public static FontStyle Oblique { get { return new FontStyle(); } }
    }

    public struct FontStretch
    {
        public static bool operator ==(FontStretch a, FontStretch b) { return false; }
        public static bool operator !=(FontStretch a, FontStretch b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public static class FontStretches
    {
        public static FontStretch Normal { get { return new FontStretch(); } }
        public static FontStretch Condensed { get { return new FontStretch(); } }
        public static FontStretch Expanded { get { return new FontStretch(); } }
    }

    // ── Resources / styles ───────────────────────────────────────────────────

    public class ResourceDictionary : IEnumerable
    {
        public object this[object key] { get { return null; } set { } }
        public ICollection Keys { get { return null; } }
        public ICollection Values { get { return null; } }
        public int Count { get { return 0; } }
        public bool Contains(object key) { return false; }
        public void Add(object key, object value) { }
        public void Remove(object key) { }
        public void Clear() { }
        public Collection<ResourceDictionary> MergedDictionaries { get { return null; } }
        public Uri Source { get; set; }
        public IEnumerator GetEnumerator() { return null; }
    }

    public class Collection<T> : List<T> { }

    public class SetterBase { }

    public class Setter : SetterBase
    {
        public Setter() { }
        public Setter(DependencyProperty property, object value) { }
        public DependencyProperty Property { get; set; }
        public object Value { get; set; }
        public string TargetName { get; set; }
    }

    public class TriggerBase { }

    public class Trigger : TriggerBase
    {
        public DependencyProperty Property { get; set; }
        public object Value { get; set; }
        public Collection<SetterBase> Setters { get { return null; } }
    }

    public class Style
    {
        public Style() { }
        public Style(Type targetType) { }
        public Style(Type targetType, Style basedOn) { }
        public Type TargetType { get; set; }
        public Style BasedOn { get; set; }
        public Collection<SetterBase> Setters { get { return null; } }
        public Collection<TriggerBase> Triggers { get { return null; } }
        public ResourceDictionary Resources { get; set; }
        public void Seal() { }
    }

    // ── Visual / element hierarchy ───────────────────────────────────────────
    // DispatcherObject -> DependencyObject -> Visual -> UIElement ->
    //     FrameworkElement -> Control -> ContentControl -> Window

    public class UIElement : Visual
    {
        public Visibility Visibility { get; set; }
        public bool IsEnabled { get; set; }
        public double Opacity { get; set; }
        public bool AllowDrop { get; set; }
        public bool IsHitTestVisible { get; set; }
        public bool ClipToBounds { get; set; }
        public bool Focusable { get; set; }
        public bool IsFocused { get { return false; } }
        public bool IsKeyboardFocused { get { return false; } }
        public bool IsKeyboardFocusWithin { get { return false; } }
        public bool IsMouseOver { get { return false; } }
        public bool IsVisible { get { return true; } }
        public bool IsMeasureValid { get { return true; } }
        public bool IsArrangeValid { get { return true; } }
        public Size DesiredSize { get { return new Size(); } }
        public Size RenderSize { get; set; }
        public Transform RenderTransform { get; set; }
        public Point RenderTransformOrigin { get; set; }
        public Geometry Clip { get; set; }
        public bool SnapsToDevicePixels { get; set; }
        public bool UseLayoutRounding { get; set; }
        public InputBindingCollection InputBindings { get { return null; } }

        public bool Focus() { return true; }
        public void InvalidateVisual() { }
        public void InvalidateMeasure() { }
        public void InvalidateArrange() { }
        public void Measure(Size availableSize) { }
        public void Arrange(Rect finalRect) { }
        public void UpdateLayout() { }
        public void CaptureMouse() { }
        public void ReleaseMouseCapture() { }
        public Point TranslatePoint(Point point, UIElement relativeTo) { return new Point(); }
        public void AddHandler(RoutedEvent routedEvent, Delegate handler) { }
        public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo) { }
        public void RemoveHandler(RoutedEvent routedEvent, Delegate handler) { }
        public void RaiseEvent(RoutedEventArgs e) { }

        public event RoutedEventHandler GotFocus;
        public event RoutedEventHandler LostFocus;
        public event System.Windows.Input.KeyEventHandler KeyDown;
        public event System.Windows.Input.KeyEventHandler KeyUp;
        public event System.Windows.Input.KeyEventHandler PreviewKeyDown;
        public event System.Windows.Input.KeyEventHandler PreviewKeyUp;
        public event System.Windows.Input.TextCompositionEventHandler PreviewTextInput;
        public event System.Windows.Input.TextCompositionEventHandler TextInput;
        public event System.Windows.Input.MouseButtonEventHandler MouseDown;
        public event System.Windows.Input.MouseButtonEventHandler MouseUp;
        public event System.Windows.Input.MouseButtonEventHandler MouseLeftButtonDown;
        public event System.Windows.Input.MouseButtonEventHandler MouseLeftButtonUp;
        public event System.Windows.Input.MouseButtonEventHandler MouseRightButtonDown;
        public event System.Windows.Input.MouseButtonEventHandler MouseRightButtonUp;
        public event System.Windows.Input.MouseButtonEventHandler PreviewMouseDown;
        public event System.Windows.Input.MouseButtonEventHandler PreviewMouseLeftButtonDown;
        public event System.Windows.Input.MouseEventHandler MouseMove;
        public event System.Windows.Input.MouseEventHandler MouseEnter;
        public event System.Windows.Input.MouseEventHandler MouseLeave;
        public event System.Windows.Input.MouseWheelEventHandler MouseWheel;
        public event System.Windows.Input.MouseWheelEventHandler PreviewMouseWheel;
        public event DependencyPropertyChangedEventHandler IsVisibleChanged;
        public event DependencyPropertyChangedEventHandler IsEnabledChanged;
        public event DragEventHandler Drop;
        public event DragEventHandler DragEnter;
        public event DragEventHandler DragOver;
        public event DragEventHandler DragLeave;

        protected virtual Size MeasureOverride(Size availableSize) { return new Size(); }
        protected virtual Size ArrangeOverride(Size finalSize) { return new Size(); }
        protected virtual void OnRender(DrawingContext drawingContext) { }

        private void Silence()
        {
            // Referencing every event once keeps CS0067 quiet without changing behaviour.
            if (GotFocus != null || LostFocus != null || KeyDown != null || KeyUp != null
                || PreviewKeyDown != null || PreviewKeyUp != null || PreviewTextInput != null
                || TextInput != null || MouseDown != null || MouseUp != null
                || MouseLeftButtonDown != null || MouseLeftButtonUp != null
                || MouseRightButtonDown != null || MouseRightButtonUp != null
                || PreviewMouseDown != null || PreviewMouseLeftButtonDown != null
                || MouseMove != null || MouseEnter != null || MouseLeave != null
                || MouseWheel != null || PreviewMouseWheel != null
                || IsVisibleChanged != null || IsEnabledChanged != null
                || Drop != null || DragEnter != null || DragOver != null || DragLeave != null) { }
        }
    }

    public class FrameworkElement : UIElement
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double MinWidth { get; set; }
        public double MinHeight { get; set; }
        public double MaxWidth { get; set; }
        public double MaxHeight { get; set; }
        public double ActualWidth { get { return 0; } }
        public double ActualHeight { get { return 0; } }
        public Thickness Margin { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public string Name { get; set; }
        public object Tag { get; set; }
        public object ToolTip { get; set; }
        public Style Style { get; set; }
        public ResourceDictionary Resources { get; set; }
        public object DataContext { get; set; }
        public Transform LayoutTransform { get; set; }
        public FlowDirection FlowDirection { get; set; }
        public System.Windows.Input.Cursor Cursor { get; set; }
        public bool ForceCursor { get; set; }
        public DependencyObject Parent { get { return null; } }
        public DependencyObject TemplatedParent { get { return null; } }
        public bool IsLoaded { get { return false; } }
        public bool OverridesDefaultStyle { get; set; }
        public object ContextMenu { get; set; }

        public object FindResource(object resourceKey) { return null; }
        public object TryFindResource(object resourceKey) { return null; }
        public object FindName(string name) { return null; }
        public void BringIntoView() { }
        public void SetBinding(DependencyProperty dp, object binding) { }

        public event RoutedEventHandler Loaded;
        public event RoutedEventHandler Unloaded;
        public event SizeChangedEventHandler SizeChanged;
        public event EventHandler LayoutUpdated;
        public event ToolTipEventHandler ToolTipOpening;
        public event ToolTipEventHandler ToolTipClosing;

        private void Silence()
        {
            if (Loaded != null || Unloaded != null || SizeChanged != null
                || LayoutUpdated != null || ToolTipOpening != null || ToolTipClosing != null) { }
        }
    }

    public class SizeChangedEventArgs : RoutedEventArgs
    {
        public Size NewSize { get { return new Size(); } }
        public Size PreviousSize { get { return new Size(); } }
        public bool WidthChanged { get { return false; } }
        public bool HeightChanged { get { return false; } }
    }

    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs e);

    public class ToolTipEventArgs : RoutedEventArgs { }
    public delegate void ToolTipEventHandler(object sender, ToolTipEventArgs e);

    public class InputBindingCollection : IEnumerable
    {
        public int Count { get { return 0; } }
        public void Add(object inputBinding) { }
        public void Clear() { }
        public IEnumerator GetEnumerator() { return null; }
    }

    // ── Application / Window ─────────────────────────────────────────────────

    public class WindowCollection : IEnumerable<Window>
    {
        public int Count { get { return 0; } }
        public Window this[int index] { get { return null; } }
        public IEnumerator<Window> GetEnumerator() { return null; }
        IEnumerator IEnumerable.GetEnumerator() { return null; }
    }

    public class Application : DispatcherObject
    {
        public static Application Current { get { return null; } }
        public WindowCollection Windows { get { return null; } }
        public Window MainWindow { get; set; }
        public ResourceDictionary Resources { get; set; }
        public ShutdownMode ShutdownMode { get; set; }

        public object FindResource(object resourceKey) { return null; }
        public object TryFindResource(object resourceKey) { return null; }
        public int Run() { return 0; }
        public void Shutdown() { }

        public static object LoadComponent(Uri resourceLocator) { return null; }
    }

    public enum ShutdownMode { OnLastWindowClose, OnMainWindowClose, OnExplicitShutdown }

    public class Window : System.Windows.Controls.ContentControl
    {
        public static Window GetWindow(DependencyObject dependencyObject) { return null; }
        public string Title { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public WindowState WindowState { get; set; }
        public WindowStyle WindowStyle { get; set; }
        public WindowStartupLocation WindowStartupLocation { get; set; }
        public ResizeMode ResizeMode { get; set; }
        public SizeToContent SizeToContent { get; set; }
        public bool Topmost { get; set; }
        public bool ShowInTaskbar { get; set; }
        public bool ShowActivated { get; set; }
        public bool IsActive { get { return false; } }
        public Window Owner { get; set; }
        public WindowCollection OwnedWindows { get { return null; } }
        public bool? DialogResult { get; set; }
        public System.Windows.Media.ImageSource Icon { get; set; }
        public bool AllowsTransparency { get; set; }

        public void Show() { }
        public bool? ShowDialog() { return null; }
        public void Close() { }
        public void Hide() { }
        public bool Activate() { return true; }
        public void DragMove() { }

        public event EventHandler Activated;
        public event EventHandler Deactivated;
        public event EventHandler Closed;
        public event System.ComponentModel.CancelEventHandler Closing;
        public event EventHandler SourceInitialized;
        public event EventHandler StateChanged;
        public event EventHandler LocationChanged;
        public event RoutedEventHandler ContentRendered;

        protected virtual void OnClosed(EventArgs e) { }
        protected virtual void OnClosing(System.ComponentModel.CancelEventArgs e) { }
        protected virtual void OnSourceInitialized(EventArgs e) { }

        private void Silence()
        {
            if (Activated != null || Deactivated != null || Closed != null || Closing != null
                || SourceInitialized != null || StateChanged != null || LocationChanged != null
                || ContentRendered != null) { }
        }
    }

    // ── MessageBox / Clipboard / drag-drop ───────────────────────────────────

    public enum MessageBoxButton { OK, OKCancel, YesNoCancel, YesNo }
    public enum MessageBoxImage { None = 0, Error = 16, Hand = 16, Stop = 16, Question = 32, Exclamation = 48, Warning = 48, Asterisk = 64, Information = 64 }
    public enum MessageBoxResult { None, OK, Cancel, Yes, No }

    public static class MessageBox
    {
        public static MessageBoxResult Show(string messageBoxText) { return MessageBoxResult.None; }
        public static MessageBoxResult Show(string messageBoxText, string caption) { return MessageBoxResult.None; }
        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) { return MessageBoxResult.None; }
        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) { return MessageBoxResult.None; }
        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) { return MessageBoxResult.None; }
    }

    public static class Clipboard
    {
        public static void SetText(string text) { }
        public static string GetText() { return ""; }
        public static bool ContainsText() { return false; }
        public static void Clear() { }
        public static IDataObject GetDataObject() { return null; }
        public static void SetDataObject(object data) { }
    }

    public interface IDataObject
    {
        object GetData(string format);
        object GetData(Type format);
        bool GetDataPresent(string format);
        bool GetDataPresent(Type format);
        string[] GetFormats();
        void SetData(object data);
        void SetData(string format, object data);
    }

    public static class DataFormats
    {
        public const string Text = "Text";
        public const string UnicodeText = "UnicodeText";
        public const string Rtf = "Rich Text Format";
        public const string Html = "HTML Format";
        public const string FileDrop = "FileDrop";
        public const string StringFormat = "System.String";
    }

    /// <summary>
    /// Real WPF's DataObject is both a clipboard payload and the static home of
    /// the pasting handlers. Ballast uses the static half to police pastes into
    /// numeric boxes.
    /// </summary>
    public sealed class DataObject : IDataObject
    {
        public DataObject() { }
        public DataObject(object data) { }
        public DataObject(string format, object data) { }

        public object GetData(string format) { return null; }
        public object GetData(Type format) { return null; }
        public bool GetDataPresent(string format) { return false; }
        public bool GetDataPresent(Type format) { return false; }
        public string[] GetFormats() { return null; }
        public void SetData(object data) { }
        public void SetData(string format, object data) { }
        public string GetText() { return ""; }
        public void SetText(string text) { }
        public bool ContainsText() { return false; }

        public static void AddPastingHandler(DependencyObject element, DataObjectPastingEventHandler handler) { }
        public static void RemovePastingHandler(DependencyObject element, DataObjectPastingEventHandler handler) { }
        public static void AddCopyingHandler(DependencyObject element, DataObjectCopyingEventHandler handler) { }
        public static void RemoveCopyingHandler(DependencyObject element, DataObjectCopyingEventHandler handler) { }
        public static void AddSettingDataHandler(DependencyObject element, DataObjectSettingDataEventHandler handler) { }
        public static void RemoveSettingDataHandler(DependencyObject element, DataObjectSettingDataEventHandler handler) { }
    }

    public class DataObjectEventArgs : RoutedEventArgs
    {
        public bool CommandCancelled { get { return false; } }
        public bool IsDragDrop { get { return false; } }
        public void CancelCommand() { }
    }

    public sealed class DataObjectPastingEventArgs : DataObjectEventArgs
    {
        public IDataObject DataObject { get; set; }
        public string FormatToApply { get; set; }
        public IDataObject SourceDataObject { get { return null; } }
    }

    public sealed class DataObjectCopyingEventArgs : DataObjectEventArgs
    {
        public IDataObject DataObject { get { return null; } }
    }

    public sealed class DataObjectSettingDataEventArgs : DataObjectEventArgs
    {
        public IDataObject DataObject { get { return null; } }
        public string Format { get { return null; } }
    }

    public delegate void DataObjectPastingEventHandler(object sender, DataObjectPastingEventArgs e);
    public delegate void DataObjectCopyingEventHandler(object sender, DataObjectCopyingEventArgs e);
    public delegate void DataObjectSettingDataEventHandler(object sender, DataObjectSettingDataEventArgs e);

    [Flags]
    public enum DragDropEffects { None = 0, Copy = 1, Move = 2, Link = 4, Scroll = -2147483648, All = -2147483645 }

    public class DragEventArgs : RoutedEventArgs
    {
        public IDataObject Data { get { return null; } }
        public DragDropEffects Effects { get; set; }
        public DragDropEffects AllowedEffects { get { return DragDropEffects.None; } }
        public Point GetPosition(UIElement relativeTo) { return new Point(); }
    }

    public delegate void DragEventHandler(object sender, DragEventArgs e);

    public static class DragDrop
    {
        public static DragDropEffects DoDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects) { return DragDropEffects.None; }
    }

    public static class SystemParameters
    {
        public static double PrimaryScreenWidth { get { return 0; } }
        public static double PrimaryScreenHeight { get { return 0; } }
        public static double WorkAreaWidth { get { return 0; } }
        public static double WorkAreaHeight { get { return 0; } }
    }

    public class LogicalTreeHelper
    {
        public static DependencyObject GetParent(DependencyObject current) { return null; }
        public static IEnumerable GetChildren(DependencyObject current) { return null; }
        public static object FindLogicalNode(DependencyObject logicalTreeNode, string elementName) { return null; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Media
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Media
{
    using System.Windows;

    public class Visual : DependencyObject
    {
        protected int VisualChildrenCount { get { return 0; } }
        protected virtual Visual GetVisualChild(int index) { return null; }
        public Point PointToScreen(Point point) { return new Point(); }
        public Point PointFromScreen(Point point) { return new Point(); }
        public GeneralTransform TransformToAncestor(Visual ancestor) { return null; }
        public GeneralTransform TransformToVisual(Visual visual) { return null; }
        public bool IsAncestorOf(DependencyObject descendant) { return false; }
        public bool IsDescendantOf(DependencyObject ancestor) { return false; }
    }

    public struct Color
    {
        private byte a, r, g, b;
        public byte A { get { return a; } set { a = value; } }
        public byte R { get { return r; } set { r = value; } }
        public byte G { get { return g; } set { g = value; } }
        public byte B { get { return b; } set { b = value; } }
        public float ScA { get { return 0; } set { } }
        public float ScR { get { return 0; } set { } }
        public float ScG { get { return 0; } set { } }
        public float ScB { get { return 0; } set { } }

        public static Color FromArgb(byte a, byte r, byte g, byte b) { return new Color(); }
        public static Color FromRgb(byte r, byte g, byte b) { return new Color(); }
        public static Color FromScRgb(float a, float r, float g, float b) { return new Color(); }
        public string ToString(IFormatProvider provider) { return ""; }
        public override string ToString() { return ""; }
        public static bool operator ==(Color x, Color y) { return false; }
        public static bool operator !=(Color x, Color y) { return true; }
        public static Color operator +(Color x, Color y) { return new Color(); }
        public static Color operator *(Color x, float c) { return new Color(); }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public static class Colors
    {
        public static Color Transparent { get { return new Color(); } }
        public static Color Black { get { return new Color(); } }
        public static Color White { get { return new Color(); } }
        public static Color Red { get { return new Color(); } }
        public static Color Green { get { return new Color(); } }
        public static Color Blue { get { return new Color(); } }
        public static Color Yellow { get { return new Color(); } }
        public static Color Orange { get { return new Color(); } }
        public static Color Gray { get { return new Color(); } }
        public static Color DarkGray { get { return new Color(); } }
        public static Color LightGray { get { return new Color(); } }
        public static Color Lime { get { return new Color(); } }
        public static Color Cyan { get { return new Color(); } }
        public static Color Magenta { get { return new Color(); } }
        public static Color Silver { get { return new Color(); } }
        public static Color Gold { get { return new Color(); } }
        public static Color DimGray { get { return new Color(); } }
        public static Color DarkRed { get { return new Color(); } }
        public static Color DarkGreen { get { return new Color(); } }
        public static Color DarkBlue { get { return new Color(); } }
    }

    public abstract class Brush : Freezable
    {
        public double Opacity { get; set; }
        public Transform Transform { get; set; }
        public Transform RelativeTransform { get; set; }
        public new Brush Clone() { return null; }
        public new Brush GetAsFrozen() { return null; }
        public new Brush GetCurrentValueAsFrozen() { return null; }
    }

    public class SolidColorBrush : Brush
    {
        public SolidColorBrush() { }
        public SolidColorBrush(Color color) { }
        public Color Color { get; set; }
    }

    public enum BrushMappingMode { Absolute, RelativeToBoundingBox }
    public enum GradientSpreadMethod { Pad, Reflect, Repeat }

    public class GradientStop : Freezable
    {
        public GradientStop() { }
        public GradientStop(Color color, double offset) { }
        public Color Color { get; set; }
        public double Offset { get; set; }
    }

    public class GradientStopCollection : List<GradientStop>
    {
        public GradientStopCollection() { }
        public GradientStopCollection(IEnumerable<GradientStop> stops) { }
    }

    public abstract class GradientBrush : Brush
    {
        public GradientStopCollection GradientStops { get; set; }
        public BrushMappingMode MappingMode { get; set; }
        public GradientSpreadMethod SpreadMethod { get; set; }
    }

    public class LinearGradientBrush : GradientBrush
    {
        public LinearGradientBrush() { }
        public LinearGradientBrush(Color start, Color end, double angle) { }
        public LinearGradientBrush(Color start, Color end, Point startPoint, Point endPoint) { }
        public Point StartPoint { get; set; }
        public Point EndPoint { get; set; }
    }

    public class RadialGradientBrush : GradientBrush
    {
        public Point Center { get; set; }
        public Point GradientOrigin { get; set; }
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
    }

    public abstract class TileBrush : Brush { }

    public class ImageBrush : TileBrush
    {
        public ImageBrush() { }
        public ImageBrush(ImageSource source) { }
        public ImageSource ImageSource { get; set; }
    }

    public static class Brushes
    {
        public static SolidColorBrush Transparent { get { return null; } }
        public static SolidColorBrush Black { get { return null; } }
        public static SolidColorBrush White { get { return null; } }
        public static SolidColorBrush Red { get { return null; } }
        public static SolidColorBrush Green { get { return null; } }
        public static SolidColorBrush Blue { get { return null; } }
        public static SolidColorBrush Yellow { get { return null; } }
        public static SolidColorBrush Orange { get { return null; } }
        public static SolidColorBrush Gray { get { return null; } }
        public static SolidColorBrush DarkGray { get { return null; } }
        public static SolidColorBrush LightGray { get { return null; } }
        public static SolidColorBrush DimGray { get { return null; } }
        public static SolidColorBrush Silver { get { return null; } }
        public static SolidColorBrush Gold { get { return null; } }
        public static SolidColorBrush Lime { get { return null; } }
        public static SolidColorBrush LimeGreen { get { return null; } }
        public static SolidColorBrush Cyan { get { return null; } }
        public static SolidColorBrush Magenta { get { return null; } }
        public static SolidColorBrush Crimson { get { return null; } }
        public static SolidColorBrush DarkRed { get { return null; } }
        public static SolidColorBrush DarkGreen { get { return null; } }
        public static SolidColorBrush DarkBlue { get { return null; } }
        public static SolidColorBrush DodgerBlue { get { return null; } }
        public static SolidColorBrush SteelBlue { get { return null; } }
        public static SolidColorBrush Goldenrod { get { return null; } }
    }

    public class FontFamily
    {
        public FontFamily() { }
        public FontFamily(string familyName) { }
        public FontFamily(Uri baseUri, string familyName) { }
        public string Source { get { return null; } }
        public double LineSpacing { get; set; }
        public IDictionary<System.Globalization.CultureInfo, string> FamilyNames { get { return null; } }
        public override string ToString() { return ""; }
    }

    public class Typeface
    {
        public Typeface(string typefaceName) { }
        public Typeface(FontFamily fontFamily, FontStyle style, FontWeight weight, FontStretch stretch) { }
        public FontFamily FontFamily { get { return null; } }
        public FontStyle Style { get { return new FontStyle(); } }
        public FontWeight Weight { get { return new FontWeight(); } }
        public FontStretch Stretch { get { return new FontStretch(); } }
    }

    public abstract class GeneralTransform : Freezable
    {
        public Point Transform(Point point) { return new Point(); }
        public Rect TransformBounds(Rect rect) { return new Rect(); }
        public bool TryTransform(Point inPoint, out Point result) { result = new Point(); return false; }
        public GeneralTransform Inverse { get { return null; } }
    }

    public abstract class Transform : GeneralTransform
    {
        public static Transform Identity { get { return null; } }
        public Matrix Value { get { return new Matrix(); } }
    }

    public struct Matrix
    {
        public double M11 { get; set; }
        public double M12 { get; set; }
        public double M21 { get; set; }
        public double M22 { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public static Matrix Identity { get { return new Matrix(); } }
        public bool IsIdentity { get { return true; } }
        public static bool operator ==(Matrix a, Matrix b) { return false; }
        public static bool operator !=(Matrix a, Matrix b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public class MatrixTransform : Transform
    {
        public MatrixTransform() { }
        public MatrixTransform(Matrix matrix) { }
        public Matrix Matrix { get; set; }
    }

    public class ScaleTransform : Transform
    {
        public ScaleTransform() { }
        public ScaleTransform(double scaleX, double scaleY) { }
        public ScaleTransform(double scaleX, double scaleY, double centerX, double centerY) { }
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
    }

    public class TranslateTransform : Transform
    {
        public TranslateTransform() { }
        public TranslateTransform(double x, double y) { }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class RotateTransform : Transform
    {
        public RotateTransform() { }
        public RotateTransform(double angle) { }
        public RotateTransform(double angle, double centerX, double centerY) { }
        public double Angle { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
    }

    public class SkewTransform : Transform
    {
        public SkewTransform() { }
        public SkewTransform(double angleX, double angleY) { }
        public double AngleX { get; set; }
        public double AngleY { get; set; }
    }

    public class TransformCollection : List<Transform> { }

    public class TransformGroup : Transform
    {
        public TransformCollection Children { get; set; }
    }

    public abstract class Geometry : Freezable
    {
        public Rect Bounds { get { return new Rect(); } }
        public static Geometry Empty { get { return null; } }
        public bool FillContains(Point point) { return false; }
    }

    public class RectangleGeometry : Geometry
    {
        public RectangleGeometry() { }
        public RectangleGeometry(Rect rect) { }
        public RectangleGeometry(Rect rect, double radiusX, double radiusY) { }
        public Rect Rect { get; set; }
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
    }

    public class EllipseGeometry : Geometry
    {
        public EllipseGeometry() { }
        public EllipseGeometry(Rect rect) { }
        public Point Center { get; set; }
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
    }

    public class LineGeometry : Geometry
    {
        public LineGeometry() { }
        public LineGeometry(Point start, Point end) { }
        public Point StartPoint { get; set; }
        public Point EndPoint { get; set; }
    }

    public class PathGeometry : Geometry { }
    public class StreamGeometry : Geometry { }
    public class GeometryGroup : Geometry { }

    public enum PenLineCap { Flat, Square, Round, Triangle }
    public enum PenLineJoin { Miter, Bevel, Round }

    public class Pen : Freezable
    {
        public Pen() { }
        public Pen(Brush brush, double thickness) { }
        public Brush Brush { get; set; }
        public double Thickness { get; set; }
        public PenLineCap StartLineCap { get; set; }
        public PenLineCap EndLineCap { get; set; }
        public PenLineJoin LineJoin { get; set; }
        public DashStyle DashStyle { get; set; }
    }

    public class DashStyle : Freezable
    {
        public DashStyle() { }
        public DashStyle(IEnumerable<double> dashes, double offset) { }
    }

    public static class DashStyles
    {
        public static DashStyle Solid { get { return null; } }
        public static DashStyle Dash { get { return null; } }
        public static DashStyle Dot { get { return null; } }
        public static DashStyle DashDot { get { return null; } }
    }

    public class DrawingContext : System.Windows.Threading.DispatcherObject
    {
        public void DrawLine(Pen pen, Point p0, Point p1) { }
        public void DrawRectangle(Brush brush, Pen pen, Rect rectangle) { }
        public void DrawRoundedRectangle(Brush brush, Pen pen, Rect rectangle, double radiusX, double radiusY) { }
        public void DrawEllipse(Brush brush, Pen pen, Point center, double radiusX, double radiusY) { }
        public void DrawGeometry(Brush brush, Pen pen, Geometry geometry) { }
        public void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public void DrawText(FormattedText formattedText, Point origin) { }
        public void PushTransform(Transform transform) { }
        public void PushClip(Geometry clipGeometry) { }
        public void PushOpacity(double opacity) { }
        public void Pop() { }
        public void Close() { }
    }

    public class FormattedText
    {
        public FormattedText(string textToFormat, System.Globalization.CultureInfo culture,
                             FlowDirection flowDirection, Typeface typeface, double emSize, Brush foreground) { }
        public double Width { get { return 0; } }
        public double Height { get { return 0; } }
        public double MaxTextWidth { get; set; }
        public TextAlignment TextAlignment { get; set; }
        public TextTrimming Trimming { get; set; }
    }

    public abstract class ImageSource : Freezable
    {
        public virtual double Width { get { return 0; } }
        public virtual double Height { get { return 0; } }
    }

    public class Drawing : Freezable { }
    public class DrawingGroup : Drawing { }
    public class DrawingVisual : Visual
    {
        public DrawingContext RenderOpen() { return null; }
        public DrawingGroup Drawing { get { return null; } }
    }

    public class ContainerVisual : Visual { }

    public struct PixelFormat
    {
        public int BitsPerPixel { get { return 0; } }
        public static bool operator ==(PixelFormat a, PixelFormat b) { return false; }
        public static bool operator !=(PixelFormat a, PixelFormat b) { return true; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public static class PixelFormats
    {
        public static PixelFormat Default { get { return new PixelFormat(); } }
        public static PixelFormat Pbgra32 { get { return new PixelFormat(); } }
        public static PixelFormat Bgra32 { get { return new PixelFormat(); } }
        public static PixelFormat Bgr32 { get { return new PixelFormat(); } }
        public static PixelFormat Bgr24 { get { return new PixelFormat(); } }
        public static PixelFormat Rgb24 { get { return new PixelFormat(); } }
        public static PixelFormat Gray8 { get { return new PixelFormat(); } }
    }

    public enum Stretch { None, Fill, Uniform, UniformToFill }
    public enum StretchDirection { UpOnly, DownOnly, Both }
    public enum BitmapScalingMode { Unspecified, LowQuality, HighQuality, Linear, Fant, NearestNeighbor }
    public enum EdgeMode { Unspecified, Aliased }

    public static class RenderOptions
    {
        public static void SetBitmapScalingMode(DependencyObject target, BitmapScalingMode mode) { }
        public static BitmapScalingMode GetBitmapScalingMode(DependencyObject target) { return BitmapScalingMode.Unspecified; }
        public static void SetEdgeMode(DependencyObject target, EdgeMode mode) { }
    }

    public static class VisualTreeHelper
    {
        public static int GetChildrenCount(DependencyObject reference) { return 0; }
        public static DependencyObject GetChild(DependencyObject reference, int childIndex) { return null; }
        public static DependencyObject GetParent(DependencyObject reference) { return null; }
        public static Rect GetDescendantBounds(Visual reference) { return new Rect(); }
        public static Rect GetContentBounds(Visual reference) { return new Rect(); }
        public static object HitTest(Visual reference, Point point) { return null; }
    }

    public class VisualBrush : TileBrush
    {
        public VisualBrush() { }
        public VisualBrush(Visual visual) { }
        public Visual Visual { get; set; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Media.Imaging
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Media.Imaging
{
    using System.Windows;
    using System.Windows.Media;

    public enum BitmapCacheOption { Default, OnDemand, OnLoad, None }

    [Flags]
    public enum BitmapCreateOptions { None = 0, PreservePixelFormat = 1, DelayCreation = 2, IgnoreColorProfile = 4, IgnoreImageCache = 8 }

    public class BitmapSizeOptions
    {
        public static BitmapSizeOptions FromEmptyOptions() { return null; }
        public static BitmapSizeOptions FromWidthAndHeight(int pixelWidth, int pixelHeight) { return null; }
        public static BitmapSizeOptions FromWidth(int pixelWidth) { return null; }
        public static BitmapSizeOptions FromHeight(int pixelHeight) { return null; }
        public static BitmapSizeOptions FromRotation(Rotation rotation) { return null; }
    }

    public enum Rotation { Rotate0, Rotate90, Rotate180, Rotate270 }

    public abstract class BitmapSource : ImageSource
    {
        public int PixelWidth { get { return 0; } }
        public int PixelHeight { get { return 0; } }
        public double DpiX { get { return 96; } }
        public double DpiY { get { return 96; } }
        public PixelFormat Format { get { return new PixelFormat(); } }
        public bool IsDownloading { get { return false; } }

        public void CopyPixels(Array pixels, int stride, int offset) { }
        public void CopyPixels(Int32Rect sourceRect, Array pixels, int stride, int offset) { }

        public static BitmapSource Create(int pixelWidth, int pixelHeight, double dpiX, double dpiY,
                                          PixelFormat pixelFormat, object palette, Array pixels, int stride) { return null; }

        public event EventHandler DownloadCompleted;
        private void Silence() { if (DownloadCompleted != null) { } }
    }

    public class BitmapImage : BitmapSource
    {
        public BitmapImage() { }
        public BitmapImage(Uri uriSource) { }
        public BitmapImage(Uri uriSource, System.Net.Cache.RequestCachePolicy uriCachePolicy) { }

        public Uri UriSource { get; set; }
        public System.IO.Stream StreamSource { get; set; }
        public BitmapCacheOption CacheOption { get; set; }
        public BitmapCreateOptions CreateOptions { get; set; }
        public int DecodePixelWidth { get; set; }
        public int DecodePixelHeight { get; set; }
        public Rotation Rotation { get; set; }
        public Int32Rect SourceRect { get; set; }
        public Uri BaseUri { get; set; }

        public void BeginInit() { }
        public void EndInit() { }
    }

    public class BitmapFrame : BitmapSource
    {
        public static BitmapFrame Create(BitmapSource source) { return null; }
        public static BitmapFrame Create(BitmapSource source, BitmapSource thumbnail) { return null; }
        public static BitmapFrame Create(Uri bitmapUri) { return null; }
        public static BitmapFrame Create(System.IO.Stream bitmapStream) { return null; }
        public BitmapSource Thumbnail { get { return null; } }
    }

    public class RenderTargetBitmap : BitmapSource
    {
        public RenderTargetBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat) { }
        public void Render(Visual visual) { }
        public void Clear() { }
    }

    public class TransformedBitmap : BitmapSource
    {
        public TransformedBitmap() { }
        public TransformedBitmap(BitmapSource source, Transform newTransform) { }
    }

    public class CroppedBitmap : BitmapSource
    {
        public CroppedBitmap() { }
        public CroppedBitmap(BitmapSource source, Int32Rect sourceRect) { }
    }

    public class WriteableBitmap : BitmapSource
    {
        public WriteableBitmap(BitmapSource source) { }
        public WriteableBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat, object palette) { }
    }

    public abstract class BitmapEncoder
    {
        public IList<BitmapFrame> Frames { get; set; }
        public BitmapSource Preview { get; set; }
        public BitmapSource Thumbnail { get; set; }
        public void Save(System.IO.Stream stream) { }
    }

    public sealed class PngBitmapEncoder : BitmapEncoder
    {
        public PngBitmapEncoder() { Frames = new List<BitmapFrame>(); }
        public PngInterlaceOption Interlace { get; set; }
    }

    public enum PngInterlaceOption { Default, On, Off }

    public sealed class JpegBitmapEncoder : BitmapEncoder
    {
        public JpegBitmapEncoder() { Frames = new List<BitmapFrame>(); }
        public int QualityLevel { get; set; }
    }

    public sealed class BmpBitmapEncoder : BitmapEncoder
    {
        public BmpBitmapEncoder() { Frames = new List<BitmapFrame>(); }
    }

    public abstract class BitmapDecoder
    {
        public IList<BitmapFrame> Frames { get { return null; } }
        public static BitmapDecoder Create(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) { return null; }
        public static BitmapDecoder Create(System.IO.Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) { return null; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Interop
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Interop
{
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>GDI/WPF bridge. Ballast uses CreateBitmapSourceFromHBitmap only.</summary>
    public static class Imaging
    {
        public static BitmapSource CreateBitmapSourceFromHBitmap(IntPtr bitmap, IntPtr palette,
                                                                 Int32Rect sourceRect, BitmapSizeOptions sizeOptions) { return null; }
        public static BitmapSource CreateBitmapSourceFromHIcon(IntPtr icon, Int32Rect sourceRect, BitmapSizeOptions sizeOptions) { return null; }
        public static BitmapSource CreateBitmapSourceFromMemorySection(IntPtr section, int pixelWidth, int pixelHeight,
                                                                       PixelFormat format, int stride, int offset) { return null; }
    }

    public class WindowInteropHelper
    {
        public WindowInteropHelper(Window window) { }
        public IntPtr Handle { get { return IntPtr.Zero; } }
        public IntPtr Owner { get; set; }
        public IntPtr EnsureHandle() { return IntPtr.Zero; }
    }

    public class HwndSource : DependencyObject, IDisposable
    {
        public IntPtr Handle { get { return IntPtr.Zero; } }
        public Visual RootVisual { get; set; }
        public CompositionTarget CompositionTarget { get { return null; } }
        public static HwndSource FromHwnd(IntPtr hwnd) { return null; }
        public static HwndSource FromVisual(Visual visual) { return null; }
        public void AddHook(HwndSourceHook hook) { }
        public void RemoveHook(HwndSourceHook hook) { }
        public void Dispose() { }
    }

    public delegate IntPtr HwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    public class CompositionTarget : DependencyObject
    {
        public Matrix TransformToDevice { get { return new Matrix(); } }
        public Matrix TransformFromDevice { get { return new Matrix(); } }
    }

    public class PresentationSource : DependencyObject
    {
        public Visual RootVisual { get; set; }
        public CompositionTarget CompositionTarget { get { return null; } }
        public static PresentationSource FromVisual(Visual visual) { return null; }
        public static PresentationSource FromDependencyObject(DependencyObject dependencyObject) { return null; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Input   (NOTE: ICommand deliberately absent — see header)
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Input
{
    using System.Windows;

    public enum Key
    {
        None = 0, Cancel, Back, Tab, LineFeed, Clear, Return, Enter = Return, Pause, Capital,
        CapsLock, Escape, Space, Prior, PageUp, Next, PageDown, End, Home,
        Left, Up, Right, Down, Select, Print, Execute, Snapshot, PrintScreen, Insert, Delete, Help,
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        LWin, RWin, Apps, Sleep,
        NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
        Multiply, Add, Separator, Subtract, Decimal, Divide,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        NumLock, Scroll, LeftShift, RightShift, LeftCtrl, RightCtrl, LeftAlt, RightAlt,
        OemSemicolon, OemPlus, OemComma, OemMinus, OemPeriod, OemQuestion, OemTilde,
        OemOpenBrackets, OemPipe, OemCloseBrackets, OemQuotes, Oem8, OemBackslash,
        System, ImeProcessed
    }

    [Flags]
    public enum ModifierKeys { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

    public enum KeyStates { None = 0, Down = 1, Toggled = 2 }
    public enum MouseButton { Left, Middle, Right, XButton1, XButton2 }
    public enum MouseButtonState { Released, Pressed }
    public enum CaptureMode { None, Element, SubTree }

    public class InputDevice : DependencyObject { }
    public class KeyboardDevice : InputDevice
    {
        public ModifierKeys Modifiers { get { return ModifierKeys.None; } }
        public bool IsKeyDown(Key key) { return false; }
        public bool IsKeyUp(Key key) { return true; }
        public KeyStates GetKeyStates(Key key) { return KeyStates.None; }
        public IInputElement FocusedElement { get { return null; } }
        public IInputElement Focus(IInputElement element) { return null; }
    }

    public class MouseDevice : InputDevice
    {
        public MouseButtonState LeftButton { get { return MouseButtonState.Released; } }
        public MouseButtonState RightButton { get { return MouseButtonState.Released; } }
        public MouseButtonState MiddleButton { get { return MouseButtonState.Released; } }
        public Point GetPosition(IInputElement relativeTo) { return new Point(); }
        public IInputElement Captured { get { return null; } }
    }

    public interface IInputElement { }

    public class InputEventArgs : RoutedEventArgs
    {
        public InputDevice Device { get { return null; } }
        public int Timestamp { get { return 0; } }
    }

    public class KeyboardEventArgs : InputEventArgs
    {
        public KeyboardDevice KeyboardDevice { get { return null; } }
    }

    public class KeyEventArgs : KeyboardEventArgs
    {
        public Key Key { get { return Key.None; } }
        public Key SystemKey { get { return Key.None; } }
        public Key ImeProcessedKey { get { return Key.None; } }
        public Key DeadCharProcessedKey { get { return Key.None; } }
        public KeyStates KeyStates { get { return KeyStates.None; } }
        public bool IsDown { get { return false; } }
        public bool IsUp { get { return true; } }
        public bool IsRepeat { get { return false; } }
        public bool IsToggled { get { return false; } }
    }

    public delegate void KeyEventHandler(object sender, KeyEventArgs e);

    public class MouseEventArgs : InputEventArgs
    {
        public MouseDevice MouseDevice { get { return null; } }
        public MouseButtonState LeftButton { get { return MouseButtonState.Released; } }
        public MouseButtonState RightButton { get { return MouseButtonState.Released; } }
        public MouseButtonState MiddleButton { get { return MouseButtonState.Released; } }
        public Point GetPosition(IInputElement relativeTo) { return new Point(); }
    }

    public delegate void MouseEventHandler(object sender, MouseEventArgs e);

    public class MouseButtonEventArgs : MouseEventArgs
    {
        public MouseButton ChangedButton { get { return MouseButton.Left; } }
        public MouseButtonState ButtonState { get { return MouseButtonState.Released; } }
        public int ClickCount { get { return 1; } }
    }

    public delegate void MouseButtonEventHandler(object sender, MouseButtonEventArgs e);

    public class MouseWheelEventArgs : MouseEventArgs
    {
        public int Delta { get { return 0; } }
    }

    public delegate void MouseWheelEventHandler(object sender, MouseWheelEventArgs e);

    public class TextComposition
    {
        public string Text { get { return ""; } }
    }

    public class TextCompositionEventArgs : InputEventArgs
    {
        public string Text { get { return ""; } }
        public TextComposition TextComposition { get { return null; } }
    }

    public delegate void TextCompositionEventHandler(object sender, TextCompositionEventArgs e);

    public static class Keyboard
    {
        public static ModifierKeys Modifiers { get { return ModifierKeys.None; } }
        public static KeyboardDevice PrimaryDevice { get { return null; } }
        public static IInputElement FocusedElement { get { return null; } }
        public static bool IsKeyDown(Key key) { return false; }
        public static bool IsKeyUp(Key key) { return true; }
        public static IInputElement Focus(IInputElement element) { return null; }
        public static void ClearFocus() { }
    }

    public static class Mouse
    {
        public static MouseDevice PrimaryDevice { get { return null; } }
        public static MouseButtonState LeftButton { get { return MouseButtonState.Released; } }
        public static MouseButtonState RightButton { get { return MouseButtonState.Released; } }
        public static IInputElement Captured { get { return null; } }
        public static Cursor OverrideCursor { get; set; }
        public static Point GetPosition(IInputElement relativeTo) { return new Point(); }
        public static bool Capture(IInputElement element) { return false; }
    }

    public class Cursor : IDisposable
    {
        public Cursor(string cursorFile) { }
        public Cursor(System.IO.Stream cursorStream) { }
        public void Dispose() { }
    }

    public static class Cursors
    {
        public static Cursor None { get { return null; } }
        public static Cursor Arrow { get { return null; } }
        public static Cursor Hand { get { return null; } }
        public static Cursor IBeam { get { return null; } }
        public static Cursor Wait { get { return null; } }
        public static Cursor SizeAll { get { return null; } }
        public static Cursor SizeNS { get { return null; } }
        public static Cursor SizeWE { get { return null; } }
    }

    // NOTE: no ICommand here. Mono's System.dll already exports
    // System.Windows.Input.ICommand and redefining it triggers CS0436.

    public class RoutedCommand
    {
        public RoutedCommand() { }
        public RoutedCommand(string name, Type ownerType) { }
        public string Name { get { return null; } }
        public bool CanExecute(object parameter, IInputElement target) { return false; }
        public void Execute(object parameter, IInputElement target) { }
    }

    public class RoutedUICommand : RoutedCommand
    {
        public RoutedUICommand() { }
        public RoutedUICommand(string text, string name, Type ownerType) { }
        public string Text { get; set; }
    }

    public class InputBinding
    {
        public InputBinding() { }
        public object Command { get; set; }
        public object CommandParameter { get; set; }
    }

    public class KeyGesture
    {
        public KeyGesture(Key key) { }
        public KeyGesture(Key key, ModifierKeys modifiers) { }
        public Key Key { get { return Key.None; } }
        public ModifierKeys Modifiers { get { return ModifierKeys.None; } }
    }

    public class KeyBinding : InputBinding
    {
        public KeyBinding() { }
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; }
        public KeyGesture Gesture { get; set; }
    }

    public class CommandBinding
    {
        public CommandBinding() { }
        public object Command { get; set; }
    }

    public static class ApplicationCommands
    {
        public static RoutedUICommand Copy { get { return null; } }
        public static RoutedUICommand Cut { get { return null; } }
        public static RoutedUICommand Paste { get { return null; } }
        public static RoutedUICommand SelectAll { get { return null; } }
        public static RoutedUICommand Undo { get { return null; } }
        public static RoutedUICommand Redo { get { return null; } }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Documents
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Documents
{
    using System.Windows;
    using System.Windows.Media;

    public abstract class TextElement : FrameworkElement
    {
        public Brush Foreground { get; set; }
        public Brush Background { get; set; }
        public FontFamily FontFamily { get; set; }
        public double FontSize { get; set; }
        public FontStyle FontStyle { get; set; }
        public FontWeight FontWeight { get; set; }
        public FontStretch FontStretch { get; set; }
    }

    public abstract class Inline : TextElement
    {
        public BaselineAlignment BaselineAlignment { get; set; }
        public TextDecorationCollection TextDecorations { get; set; }
    }

    public class InlineCollection : IEnumerable<Inline>
    {
        public int Count { get { return 0; } }
        public void Add(Inline item) { }
        public void Add(string text) { }
        public void Add(UIElement element) { }
        public void Clear() { }
        public void Remove(Inline item) { }
        public bool Contains(Inline item) { return false; }
        public IEnumerator<Inline> GetEnumerator() { return null; }
        IEnumerator IEnumerable.GetEnumerator() { return null; }
    }

    public class Run : Inline
    {
        public Run() { }
        public Run(string text) { }
        public string Text { get; set; }
    }

    public class Span : Inline
    {
        public InlineCollection Inlines { get { return null; } }
    }

    public class Bold : Span
    {
        public Bold() { }
        public Bold(Inline childInline) { }
    }

    public class Italic : Span
    {
        public Italic() { }
        public Italic(Inline childInline) { }
    }

    public class Underline : Span { }

    public class LineBreak : Inline { }

    public class Hyperlink : Span
    {
        public Uri NavigateUri { get; set; }
        public event RoutedEventHandler Click;
        private void Silence() { if (Click != null) { } }
    }

    public abstract class Block : TextElement
    {
        public Thickness Padding { get; set; }
        public Thickness BorderThickness { get; set; }
        public Brush BorderBrush { get; set; }
        public TextAlignment TextAlignment { get; set; }
        public double LineHeight { get; set; }
    }

    public class BlockCollection : IEnumerable<Block>
    {
        public int Count { get { return 0; } }
        public void Add(Block item) { }
        public void Clear() { }
        public IEnumerator<Block> GetEnumerator() { return null; }
        IEnumerator IEnumerable.GetEnumerator() { return null; }
    }

    public class Paragraph : Block
    {
        public Paragraph() { }
        public Paragraph(Inline inline) { }
        public InlineCollection Inlines { get { return null; } }
    }

    public class TextDecoration : Freezable { }

    public class TextDecorationCollection : List<TextDecoration>
    {
        public TextDecorationCollection() { }
        public TextDecorationCollection(IEnumerable<TextDecoration> items) { }
    }

    public static class TextDecorations
    {
        public static TextDecorationCollection Underline { get { return null; } }
        public static TextDecorationCollection Strikethrough { get { return null; } }
        public static TextDecorationCollection Baseline { get { return null; } }
        public static TextDecorationCollection OverLine { get { return null; } }
    }

    public class FlowDocument : FrameworkElement
    {
        public BlockCollection Blocks { get { return null; } }
        public double PageWidth { get; set; }
        public double PageHeight { get; set; }
        public Thickness PagePadding { get; set; }
        public double ColumnWidth { get; set; }
    }

    public class TextPointer { }

    public class TextRange
    {
        public TextRange(TextPointer start, TextPointer end) { }
        public string Text { get; set; }
    }

    public class Adorner : FrameworkElement
    {
        public Adorner(UIElement adornedElement) { }
        public UIElement AdornedElement { get { return null; } }
    }

    public class AdornerLayer : FrameworkElement
    {
        public static AdornerLayer GetAdornerLayer(Visual visual) { return null; }
        public void Add(Adorner adorner) { }
        public void Remove(Adorner adorner) { }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Controls.Primitives
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Controls.Primitives
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;

    public enum ClickMode { Release, Press, Hover }

    public abstract class ButtonBase : ContentControl
    {
        public ClickMode ClickMode { get; set; }
        public bool IsPressed { get { return false; } }
        public object Command { get; set; }
        public object CommandParameter { get; set; }
        public System.Windows.Input.IInputElement CommandTarget { get; set; }
        public event RoutedEventHandler Click;
        protected virtual void OnClick() { }
        private void Silence() { if (Click != null) { } }
    }

    public class ToggleButton : ButtonBase
    {
        public bool? IsChecked { get; set; }
        public bool IsThreeState { get; set; }
        public event RoutedEventHandler Checked;
        public event RoutedEventHandler Unchecked;
        public event RoutedEventHandler Indeterminate;
        private void Silence() { if (Checked != null || Unchecked != null || Indeterminate != null) { } }
    }

    public class RepeatButton : ButtonBase
    {
        public int Delay { get; set; }
        public int Interval { get; set; }
    }

    public abstract class RangeBase : Control
    {
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Value { get; set; }
        public double SmallChange { get; set; }
        public double LargeChange { get; set; }
        public event RoutedPropertyChangedEventHandler<double> ValueChanged;
        private void Silence() { if (ValueChanged != null) { } }
    }

    public class RoutedPropertyChangedEventArgs<T> : RoutedEventArgs
    {
        public T OldValue { get { return default(T); } }
        public T NewValue { get { return default(T); } }
    }

    public delegate void RoutedPropertyChangedEventHandler<T>(object sender, RoutedPropertyChangedEventArgs<T> e);

    public class ScrollBar : RangeBase
    {
        public Orientation Orientation { get; set; }
        public double ViewportSize { get; set; }
    }

    public class Thumb : Control
    {
        public bool IsDragging { get { return false; } }
    }

    public class Track : FrameworkElement { }

    public abstract class Selector : ItemsControl
    {
        public int SelectedIndex { get; set; }
        public object SelectedItem { get; set; }
        public object SelectedValue { get; set; }
        public string SelectedValuePath { get; set; }
        public bool IsSynchronizedWithCurrentItem { get; set; }
        public event SelectionChangedEventHandler SelectionChanged;
        private void Silence() { if (SelectionChanged != null) { } }
    }

    public enum PlacementMode { Absolute, Relative, Bottom, Center, Right, AbsolutePoint, RelativePoint, Mouse, MousePoint, Left, Top, Custom }

    public class Popup : FrameworkElement
    {
        public UIElement Child { get; set; }
        public bool IsOpen { get; set; }
        public bool StaysOpen { get; set; }
        public PlacementMode Placement { get; set; }
        public UIElement PlacementTarget { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
        public bool AllowsTransparency { get; set; }
        public event EventHandler Opened;
        public event EventHandler Closed;
        private void Silence() { if (Opened != null || Closed != null) { } }
    }

    public class UniformGrid : Panel
    {
        public int Rows { get; set; }
        public int Columns { get; set; }
        public int FirstColumn { get; set; }
    }

    public class TabPanel : Panel { }
    public class StatusBar : ItemsControl { }
    public class StatusBarItem : ContentControl { }

    public class TextBoxBase : Control
    {
        public bool AcceptsReturn { get; set; }
        public bool AcceptsTab { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsReadOnlyCaretVisible { get; set; }
        public bool IsUndoEnabled { get; set; }
        public int UndoLimit { get; set; }
        public bool AutoWordSelection { get; set; }
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public double ExtentWidth { get { return 0; } }
        public double ExtentHeight { get { return 0; } }
        public double ViewportWidth { get { return 0; } }
        public double ViewportHeight { get { return 0; } }
        public double HorizontalOffset { get { return 0; } }
        public double VerticalOffset { get { return 0; } }
        public bool CanUndo { get { return false; } }
        public bool CanRedo { get { return false; } }
        public Brush SelectionBrush { get; set; }
        public double SelectionOpacity { get; set; }
        public Brush CaretBrush { get; set; }

        public void AppendText(string textData) { }
        public void Clear() { }
        public void Copy() { }
        public void Cut() { }
        public void Paste() { }
        public void SelectAll() { }
        public bool Undo() { return false; }
        public bool Redo() { return false; }
        public void ScrollToHome() { }
        public void ScrollToEnd() { }
        public void ScrollToLine(int lineIndex) { }
        public void LineUp() { }
        public void LineDown() { }
        public void PageUp() { }
        public void PageDown() { }
        public void BeginChange() { }
        public void EndChange() { }

        public event TextChangedEventHandler TextChanged;
        public event RoutedEventHandler SelectionChanged;
        private void Silence() { if (TextChanged != null || SelectionChanged != null) { } }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Controls
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Controls
{
    using System.Windows;
    using System.Windows.Controls.Primitives;
    using System.Windows.Documents;
    using System.Windows.Media;

    public enum Orientation { Horizontal, Vertical }
    public enum Dock { Left, Top, Right, Bottom }
    public enum ScrollBarVisibility { Disabled, Auto, Hidden, Visible }
    public enum CharacterCasing { Normal, Lower, Upper }
    public enum SelectionMode { Single, Multiple, Extended }

    // ── Control ──────────────────────────────────────────────────────────────

    public class Control : FrameworkElement
    {
        public Brush Background { get; set; }
        public Brush Foreground { get; set; }
        public Brush BorderBrush { get; set; }
        public Thickness BorderThickness { get; set; }
        public Thickness Padding { get; set; }
        public FontFamily FontFamily { get; set; }
        public double FontSize { get; set; }
        public FontStyle FontStyle { get; set; }
        public FontWeight FontWeight { get; set; }
        public FontStretch FontStretch { get; set; }
        public HorizontalAlignment HorizontalContentAlignment { get; set; }
        public VerticalAlignment VerticalContentAlignment { get; set; }
        public bool IsTabStop { get; set; }
        public int TabIndex { get; set; }
        public ControlTemplate Template { get; set; }
        public void ApplyTemplate() { }
        public virtual void OnApplyTemplate() { }
        public object GetTemplateChild(string childName) { return null; }
    }

    public class ControlTemplate
    {
        public ControlTemplate() { }
        public ControlTemplate(Type targetType) { }
        public Type TargetType { get; set; }
    }

    public class DataTemplate
    {
        public DataTemplate() { }
        public DataTemplate(object dataType) { }
        public object DataType { get; set; }
    }

    public class ItemsPanelTemplate { }

    public class ContentControl : Control
    {
        public object Content { get; set; }
        public DataTemplate ContentTemplate { get; set; }
        public string ContentStringFormat { get; set; }
        public bool HasContent { get { return false; } }
    }

    public class ContentPresenter : FrameworkElement
    {
        public object Content { get; set; }
        public DataTemplate ContentTemplate { get; set; }
    }

    public class HeaderedContentControl : ContentControl
    {
        public object Header { get; set; }
        public DataTemplate HeaderTemplate { get; set; }
    }

    // ── Panels ───────────────────────────────────────────────────────────────

    public class UIElementCollection : IEnumerable<UIElement>, IEnumerable
    {
        public int Count { get { return 0; } }
        public UIElement this[int index] { get { return null; } set { } }
        public int Add(UIElement element) { return 0; }
        public void Clear() { }
        public bool Contains(UIElement element) { return false; }
        public int IndexOf(UIElement element) { return -1; }
        public void Insert(int index, UIElement element) { }
        public void Remove(UIElement element) { }
        public void RemoveAt(int index) { }
        public void RemoveRange(int index, int count) { }
        public void CopyTo(Array array, int index) { }
        public IEnumerator<UIElement> GetEnumerator() { return null; }
        IEnumerator IEnumerable.GetEnumerator() { return null; }
    }

    public abstract class Panel : FrameworkElement
    {
        public UIElementCollection Children { get { return null; } }
        public Brush Background { get; set; }
        public bool IsItemsHost { get { return false; } }

        public static void SetZIndex(UIElement element, int value) { }
        public static int GetZIndex(UIElement element) { return 0; }
        public static readonly DependencyProperty ZIndexProperty = null;
        public static readonly DependencyProperty BackgroundProperty = null;
    }

    public class Canvas : Panel
    {
        public static void SetLeft(UIElement element, double length) { }
        public static double GetLeft(UIElement element) { return 0; }
        public static void SetTop(UIElement element, double length) { }
        public static double GetTop(UIElement element) { return 0; }
        public static void SetRight(UIElement element, double length) { }
        public static double GetRight(UIElement element) { return 0; }
        public static void SetBottom(UIElement element, double length) { }
        public static double GetBottom(UIElement element) { return 0; }
    }

    public class StackPanel : Panel
    {
        public Orientation Orientation { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public bool CanHorizontallyScroll { get; set; }
    }

    public class WrapPanel : Panel
    {
        public Orientation Orientation { get; set; }
        public double ItemWidth { get; set; }
        public double ItemHeight { get; set; }
    }

    public class DockPanel : Panel
    {
        public bool LastChildFill { get; set; }
        public static void SetDock(UIElement element, Dock dock) { }
        public static Dock GetDock(UIElement element) { return Dock.Left; }
    }

    /// <summary>
    /// Row/column definitions are NOT FrameworkElements in real WPF - they hang
    /// off FrameworkContentElement. Mirrored here so Width/Height do not collide
    /// with the layout properties of a real element.
    /// </summary>
    public abstract class DefinitionBase : DependencyObject
    {
        public string Name { get; set; }
        public object SharedSizeGroup { get; set; }
    }

    public class ColumnDefinition : DefinitionBase
    {
        public GridLength Width { get; set; }
        public double MinWidth { get; set; }
        public double MaxWidth { get; set; }
        public double ActualWidth { get { return 0; } }
        public double Offset { get { return 0; } }
    }

    public class RowDefinition : DefinitionBase
    {
        public GridLength Height { get; set; }
        public double MinHeight { get; set; }
        public double MaxHeight { get; set; }
        public double ActualHeight { get { return 0; } }
        public double Offset { get { return 0; } }
    }

    public class ColumnDefinitionCollection : IEnumerable<ColumnDefinition>
    {
        public int Count { get { return 0; } }
        public ColumnDefinition this[int index] { get { return null; } set { } }
        public void Add(ColumnDefinition value) { }
        public void Clear() { }
        public bool Contains(ColumnDefinition value) { return false; }
        public int IndexOf(ColumnDefinition value) { return -1; }
        public void Insert(int index, ColumnDefinition value) { }
        public bool Remove(ColumnDefinition value) { return false; }
        public void RemoveAt(int index) { }
        public IEnumerator<ColumnDefinition> GetEnumerator() { return null; }
        IEnumerator IEnumerable.GetEnumerator() { return null; }
    }

    public class RowDefinitionCollection : IEnumerable<RowDefinition>
    {
        public int Count { get { return 0; } }
        public RowDefinition this[int index] { get { return null; } set { } }
        public void Add(RowDefinition value) { }
        public void Clear() { }
        public bool Contains(RowDefinition value) { return false; }
        public int IndexOf(RowDefinition value) { return -1; }
        public void Insert(int index, RowDefinition value) { }
        public bool Remove(RowDefinition value) { return false; }
        public void RemoveAt(int index) { }
        public IEnumerator<RowDefinition> GetEnumerator() { return null; }
        IEnumerator IEnumerable.GetEnumerator() { return null; }
    }

    public class Grid : Panel
    {
        public ColumnDefinitionCollection ColumnDefinitions { get { return null; } }
        public RowDefinitionCollection RowDefinitions { get { return null; } }
        public bool ShowGridLines { get; set; }

        public static void SetRow(UIElement element, int value) { }
        public static int GetRow(UIElement element) { return 0; }
        public static void SetColumn(UIElement element, int value) { }
        public static int GetColumn(UIElement element) { return 0; }
        public static void SetRowSpan(UIElement element, int value) { }
        public static int GetRowSpan(UIElement element) { return 1; }
        public static void SetColumnSpan(UIElement element, int value) { }
        public static int GetColumnSpan(UIElement element) { return 1; }
        public static void SetIsSharedSizeScope(UIElement element, bool value) { }
    }

    public class GridSplitter : Thumb
    {
        public GridResizeDirection ResizeDirection { get; set; }
        public GridResizeBehavior ResizeBehavior { get; set; }
        public bool ShowsPreview { get; set; }
    }

    public enum GridResizeDirection { Auto, Columns, Rows }
    public enum GridResizeBehavior { BasedOnAlignment, CurrentAndNext, PreviousAndCurrent, PreviousAndNext }

    // ── Decorators ───────────────────────────────────────────────────────────

    public class Decorator : FrameworkElement
    {
        public UIElement Child { get; set; }
    }

    public class Border : Decorator
    {
        public Brush Background { get; set; }
        public Brush BorderBrush { get; set; }
        public Thickness BorderThickness { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Thickness Padding { get; set; }
    }

    public class Viewbox : Decorator
    {
        public Stretch Stretch { get; set; }
        public StretchDirection StretchDirection { get; set; }
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    public class TextBlock : FrameworkElement
    {
        public TextBlock() { }
        public TextBlock(Inline inline) { }

        public string Text { get; set; }
        public InlineCollection Inlines { get { return null; } }
        public Brush Foreground { get; set; }
        public Brush Background { get; set; }
        public FontFamily FontFamily { get; set; }
        public double FontSize { get; set; }
        public FontStyle FontStyle { get; set; }
        public FontWeight FontWeight { get; set; }
        public FontStretch FontStretch { get; set; }
        public TextWrapping TextWrapping { get; set; }
        public TextTrimming TextTrimming { get; set; }
        public TextAlignment TextAlignment { get; set; }
        public TextDecorationCollection TextDecorations { get; set; }
        public Thickness Padding { get; set; }
        public double LineHeight { get; set; }
        public LineStackingStrategy LineStackingStrategy { get; set; }
        public BaselineAlignment BaselineAlignment { get; set; }
        public bool IsHyphenationEnabled { get; set; }

        public static void SetForeground(DependencyObject element, Brush value) { }
        public static void SetFontSize(DependencyObject element, double value) { }
    }

    public class Label : ContentControl { }

    public class TextBox : TextBoxBase
    {
        public string Text { get; set; }
        public int MaxLength { get; set; }
        public int MaxLines { get; set; }
        public int MinLines { get; set; }
        public int LineCount { get { return 0; } }
        public int CaretIndex { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public string SelectedText { get; set; }
        public TextWrapping TextWrapping { get; set; }
        public TextAlignment TextAlignment { get; set; }
        public CharacterCasing CharacterCasing { get; set; }

        public void Select(int start, int length) { }
        public int GetLineIndexFromCharacterIndex(int charIndex) { return 0; }
        public string GetLineText(int lineIndex) { return ""; }
        public int GetCharacterIndexFromLineIndex(int lineIndex) { return 0; }
    }

    public class PasswordBox : Control
    {
        public string Password { get; set; }
        public char PasswordChar { get; set; }
        public int MaxLength { get; set; }
    }

    public class RichTextBox : TextBoxBase
    {
        public FlowDocument Document { get; set; }
    }

    public class TextChangedEventArgs : RoutedEventArgs
    {
        public UndoAction UndoAction { get { return UndoAction.None; } }
        public ICollection<TextChange> Changes { get { return null; } }
    }

    public enum UndoAction { None, Merge, Undo, Redo, Clear, Create }

    public class TextChange
    {
        public int Offset { get { return 0; } }
        public int AddedLength { get { return 0; } }
        public int RemovedLength { get { return 0; } }
    }

    public delegate void TextChangedEventHandler(object sender, TextChangedEventArgs e);

    // ── Buttons ──────────────────────────────────────────────────────────────

    public class Button : ButtonBase
    {
        public bool IsDefault { get; set; }
        public bool IsCancel { get; set; }
    }

    public class CheckBox : ToggleButton { }

    public class RadioButton : ToggleButton
    {
        public string GroupName { get; set; }
    }

    // ── Items controls ───────────────────────────────────────────────────────

    public class ItemCollection : IEnumerable, IEnumerable<object>
    {
        public int Count { get { return 0; } }
        public object this[int index] { get { return null; } set { } }
        public bool IsEmpty { get { return true; } }
        public object CurrentItem { get { return null; } }
        public int Add(object newItem) { return 0; }
        public void Insert(int insertIndex, object insertItem) { }
        public void Remove(object removeItem) { }
        public void RemoveAt(int removeIndex) { }
        public void Clear() { }
        public bool Contains(object containItem) { return false; }
        public int IndexOf(object item) { return -1; }
        public void CopyTo(Array array, int index) { }
        public void Refresh() { }
        public IEnumerator<object> GetEnumerator() { return null; }
        IEnumerator IEnumerable.GetEnumerator() { return null; }
    }

    public class ItemsControl : Control
    {
        public ItemCollection Items { get { return null; } }
        public IEnumerable ItemsSource { get; set; }
        public DataTemplate ItemTemplate { get; set; }
        public Style ItemContainerStyle { get; set; }
        public ItemsPanelTemplate ItemsPanel { get; set; }
        public string DisplayMemberPath { get; set; }
        public bool HasItems { get { return false; } }
        public bool IsGrouping { get { return false; } }
        public DependencyObject ItemContainerGenerator { get { return null; } }
        public object GetContainerFromItem(object item) { return null; }
    }

    public class SelectionChangedEventArgs : RoutedEventArgs
    {
        public IList AddedItems { get { return null; } }
        public IList RemovedItems { get { return null; } }
    }

    public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs e);

    public class ComboBox : Selector
    {
        public bool IsEditable { get; set; }
        public bool IsDropDownOpen { get; set; }
        public bool IsReadOnly { get; set; }
        public bool StaysOpenOnEdit { get; set; }
        public string Text { get; set; }
        public double MaxDropDownHeight { get; set; }
        public bool ShouldPreserveUserEnteredPrefix { get; set; }
        public bool IsTextSearchEnabled { get; set; }
        public event EventHandler DropDownOpened;
        public event EventHandler DropDownClosed;
        private void Silence() { if (DropDownOpened != null || DropDownClosed != null) { } }
    }

    public class ComboBoxItem : ContentControl
    {
        public bool IsSelected { get; set; }
    }

    public class ListBox : Selector
    {
        public SelectionMode SelectionMode { get; set; }
        public IList SelectedItems { get { return null; } }
        public void ScrollIntoView(object item) { }
        public void SelectAll() { }
        public void UnselectAll() { }
    }

    public class ListBoxItem : ContentControl
    {
        public bool IsSelected { get; set; }
    }

    public class ListView : ListBox
    {
        public object View { get; set; }
    }

    public class TabControl : Selector
    {
        public Dock TabStripPlacement { get; set; }
        public object SelectedContent { get; set; }
        public DataTemplate ContentTemplate { get; set; }
    }

    public class TabItem : HeaderedContentControl
    {
        public bool IsSelected { get; set; }
    }

    public class MenuItem : HeaderedItemsControl
    {
        public bool IsCheckable { get; set; }
        public bool IsChecked { get; set; }
        public object Icon { get; set; }
        public object Command { get; set; }
        public object CommandParameter { get; set; }
        public string InputGestureText { get; set; }
        public bool IsSubmenuOpen { get; set; }
        public event RoutedEventHandler Click;
        public event RoutedEventHandler SubmenuOpened;
        private void Silence() { if (Click != null || SubmenuOpened != null) { } }
    }

    public class HeaderedItemsControl : ItemsControl
    {
        public object Header { get; set; }
        public DataTemplate HeaderTemplate { get; set; }
    }

    public class Menu : ItemsControl { }
    public class ContextMenu : ItemsControl
    {
        public bool IsOpen { get; set; }
        public UIElement PlacementTarget { get; set; }
    }

    public class Separator : Control { }

    public class ToolTip : ContentControl
    {
        public bool IsOpen { get; set; }
        public UIElement PlacementTarget { get; set; }
    }

    public static class ToolTipService
    {
        public static void SetInitialShowDelay(DependencyObject element, int value) { }
        public static void SetShowDuration(DependencyObject element, int value) { }
        public static void SetIsEnabled(DependencyObject element, bool value) { }
        public static void SetToolTip(DependencyObject element, object value) { }
    }

    // ── Ranges / scrolling / images ──────────────────────────────────────────

    public class ProgressBar : RangeBase
    {
        public bool IsIndeterminate { get; set; }
        public Orientation Orientation { get; set; }
    }

    public class Slider : RangeBase
    {
        public Orientation Orientation { get; set; }
        public double TickFrequency { get; set; }
        public bool IsSnapToTickEnabled { get; set; }
    }

    public class ScrollViewer : ContentControl
    {
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public bool CanContentScroll { get; set; }
        public double HorizontalOffset { get { return 0; } }
        public double VerticalOffset { get { return 0; } }
        public double ScrollableWidth { get { return 0; } }
        public double ScrollableHeight { get { return 0; } }
        public double ExtentWidth { get { return 0; } }
        public double ExtentHeight { get { return 0; } }
        public double ViewportWidth { get { return 0; } }
        public double ViewportHeight { get { return 0; } }

        public void ScrollToTop() { }
        public void ScrollToBottom() { }
        public void ScrollToEnd() { }
        public void ScrollToHome() { }
        public void ScrollToHorizontalOffset(double offset) { }
        public void ScrollToVerticalOffset(double offset) { }
        public void LineUp() { }
        public void LineDown() { }
        public void PageUp() { }
        public void PageDown() { }
        public void InvalidateScrollInfo() { }

        public static void SetHorizontalScrollBarVisibility(DependencyObject element, ScrollBarVisibility value) { }
        public static void SetVerticalScrollBarVisibility(DependencyObject element, ScrollBarVisibility value) { }
        public static void SetCanContentScroll(DependencyObject element, bool value) { }

        public event EventHandler ScrollChanged;
        private void Silence() { if (ScrollChanged != null) { } }
    }

    public class Image : FrameworkElement
    {
        public ImageSource Source { get; set; }
        public Stretch Stretch { get; set; }
        public StretchDirection StretchDirection { get; set; }
    }

    public class MediaElement : FrameworkElement
    {
        public Uri Source { get; set; }
    }

    public class Frame : ContentControl
    {
        public Uri Source { get; set; }
    }

    public class Expander : HeaderedContentControl
    {
        public bool IsExpanded { get; set; }
        public event RoutedEventHandler Expanded;
        public event RoutedEventHandler Collapsed;
        private void Silence() { if (Expanded != null || Collapsed != null) { } }
    }

    public class GroupBox : HeaderedContentControl { }

    public class Calendar : Control
    {
        public DateTime? SelectedDate { get; set; }
    }

    public class DatePicker : Control
    {
        public DateTime? SelectedDate { get; set; }
        public string Text { get; set; }
        public event EventHandler<SelectionChangedEventArgs> SelectedDateChanged;
        private void Silence() { if (SelectedDateChanged != null) { } }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.Windows.Shapes
// ═════════════════════════════════════════════════════════════════════════════
namespace System.Windows.Shapes
{
    using System.Windows;
    using System.Windows.Media;

    public abstract class Shape : FrameworkElement
    {
        public Brush Fill { get; set; }
        public Brush Stroke { get; set; }
        public double StrokeThickness { get; set; }
        public PenLineCap StrokeStartLineCap { get; set; }
        public PenLineCap StrokeEndLineCap { get; set; }
        public PenLineJoin StrokeLineJoin { get; set; }
        public DoubleCollection StrokeDashArray { get; set; }
        public double StrokeDashOffset { get; set; }
        public Stretch Stretch { get; set; }
        public Geometry RenderedGeometry { get { return null; } }
    }

    public class DoubleCollection : List<double>
    {
        public DoubleCollection() { }
        public DoubleCollection(IEnumerable<double> values) { }
    }

    public class Rectangle : Shape
    {
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
    }

    public class Ellipse : Shape { }

    public class Line : Shape
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    public class Polygon : Shape { }
    public class Polyline : Shape { }

    public class Path : Shape
    {
        public Geometry Data { get; set; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// System.ComponentModel.DataAnnotations
//   Only the sliver NinjaScript indicator properties use.
// ═════════════════════════════════════════════════════════════════════════════
namespace System.ComponentModel.DataAnnotations
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter |
                    AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class DisplayAttribute : Attribute
    {
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string Description { get; set; }
        public string Prompt { get; set; }
        public string GroupName { get; set; }
        public int Order { get; set; }
        public Type ResourceType { get; set; }
        public bool AutoGenerateField { get; set; }
        public bool AutoGenerateFilter { get; set; }
        public string GetName() { return Name; }
        public string GetDescription() { return Description; }
        public string GetGroupName() { return GroupName; }
        public int? GetOrder() { return Order; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public class RangeAttribute : Attribute
    {
        public RangeAttribute(int minimum, int maximum) { }
        public RangeAttribute(double minimum, double maximum) { }
        public RangeAttribute(Type type, string minimum, string maximum) { }
        public object Minimum { get { return null; } }
        public object Maximum { get { return null; } }
        public string ErrorMessage { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public class RequiredAttribute : Attribute
    {
        public string ErrorMessage { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class DisplayFormatAttribute : Attribute
    {
        public string DataFormatString { get; set; }
        public bool ApplyFormatInEditMode { get; set; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.Core
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.Core
{
    using System.Windows.Threading;

    /// <summary>NinjaTrader's process-wide odds and ends.</summary>
    public static class Globals
    {
        /// <summary>Platform "now" — respects the configured time zone, unlike DateTime.Now.</summary>
        public static DateTime Now { get { return DateTime.Now; } }
        public static DateTime MinDate { get { return DateTime.MinValue; } }
        public static DateTime MaxDate { get { return DateTime.MaxValue; } }

        /// <summary>Documents\NinjaTrader 8 — where Ballast keeps every file it owns.</summary>
        public static string UserDataDir { get { return ""; } }
        public static string InstallDir { get { return ""; } }
        public static string MachineId { get { return ""; } }

        /// <summary>Any UI dispatcher NinjaTrader happens to have handy.</summary>
        public static Dispatcher RandomDispatcher { get { return null; } }
        public static Dispatcher DispatcherRandom { get { return null; } }

        public static System.Collections.Generic.List<object> AllWindows { get { return null; } }
        public static System.Collections.Generic.List<object> AllToolWindows { get { return null; } }
        public static System.Collections.Generic.List<object> AllNTWindows { get { return null; } }

        public static System.Globalization.CultureInfo GeneralOptions { get { return null; } }
        public static double ToRoundedDouble(double value, double increment) { return value; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.Cbi   (Common Business Interface — accounts, orders, instruments)
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.Cbi
{
    public enum Currency
    {
        AustralianDollar, BritishPound, CanadianDollar, EuroCurrency, HongKongDollar,
        JapaneseYen, MexicanPeso, NorwegianKrone, NewZealandDollar, SouthAfricanRand,
        SwedishKrona, SwissFranc, UsDollar
    }

    public enum AccountItem
    {
        BuyingPower, CashValue, Commission, ExcessInitialMargin, ExcessMaintenanceMargin,
        ExcessPositionMargin, GrossRealizedProfitLoss, InitialMargin, LongOptionValue,
        LookAheadMaintenanceMargin, MaintenanceMargin, NetLiquidation, NetLiquidationByCurrency,
        PositionMargin, RealizedProfitLoss, ShortOptionValue, SodBuyingPower, SodCashValue,
        SodCrossCurrencyValue, SodLiquidatingValue, SodNetLiquidation, TotalCashBalance,
        UnrealizedProfitLoss
    }

    public enum MarketPosition { Long, Short, Flat }
    public enum OrderAction { Buy, Sell, SellShort, BuyToCover }
    public enum OrderType { Limit, Market, MarketIfTouched, StopMarket, StopLimit }
    public enum OrderState { Initialized, Submitted, Accepted, Working, ChangePending, CancelPending, Cancelled, Filled, PartFilled, Rejected, Unknown, TriggerPending, PendingSubmit, PendingChange, PendingCancel }
    public enum TimeInForce { Day, Gtc, Gtd, Ioc, Fok }
    public enum ConnectionStatus { Disconnected, Connecting, Connected, ConnectionLost }
    public enum AccountType { Cash, Margin, Futures, Forex, Simulation, Unknown }
    public enum InstrumentType { Future, Stock, Option, Index, Currency, CryptoCurrency, Cfd, Fund, Unknown }
    public enum PositionUpdateReason { Update, Rollover, Reconcile }
    public enum OperationalMode { Simulation, Playback, Live }

    public class MasterInstrument
    {
        public string Name { get; set; }
        public InstrumentType InstrumentType { get; set; }
        public double PointValue { get; set; }
        public double TickSize { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public static MasterInstrument GetInstrument(string name) { return null; }
        public double RoundToTickSize(double price) { return price; }
        public override string ToString() { return ""; }
    }

    public class Instrument
    {
        public string FullName { get; set; }
        public MasterInstrument MasterInstrument { get; set; }
        public DateTime Expiry { get; set; }
        public static Instrument GetInstrument(string fullName) { return null; }
        public static System.Collections.Generic.List<Instrument> All { get { return null; } }
        public override string ToString() { return ""; }
    }

    public class Position
    {
        public Account Account { get { return null; } }
        public Instrument Instrument { get; set; }
        public MarketPosition MarketPosition { get; set; }
        public int Quantity { get; set; }
        public double AveragePrice { get; set; }
        public double Quantity2 { get; set; }
        public double GetUnrealizedProfitLoss(PerformanceUnit unit) { return 0; }
        public double GetUnrealizedProfitLoss(PerformanceUnit unit, double price) { return 0; }
    }

    public enum PerformanceUnit { Currency, Percent, Pips, Points, Ticks }

    public class Order
    {
        public string Name { get; set; }
        public Instrument Instrument { get; set; }
        public OrderAction OrderAction { get; set; }
        public OrderType OrderType { get; set; }
        public OrderState OrderState { get; set; }
        public int Quantity { get; set; }
        public int Filled { get; set; }
        public double LimitPrice { get; set; }
        public double StopPrice { get; set; }
        public double AverageFillPrice { get; set; }
        public DateTime Time { get; set; }
        public Account Account { get { return null; } }
        public string OrderId { get; set; }
    }

    public class Execution
    {
        public string ExecutionId { get; set; }
        public Instrument Instrument { get; set; }
        public Order Order { get; set; }
        public MarketPosition MarketPosition { get; set; }
        public int Quantity { get; set; }
        public double Price { get; set; }
        public double Commission { get; set; }
        public DateTime Time { get; set; }
        public Account Account { get { return null; } }
    }

    public class Trade
    {
        public Execution Entry { get; set; }
        public Execution Exit { get; set; }
        public int Quantity { get; set; }
    }

    public class TradeCollection : System.Collections.Generic.List<Trade> { }

    public class ExecutionEventArgs : EventArgs
    {
        public Execution Execution { get { return null; } }
        public string ExecutionId { get { return ""; } }
        public Order Order { get { return null; } }
        public Instrument Instrument { get { return null; } }
        public MarketPosition MarketPosition { get { return MarketPosition.Flat; } }
        public int Quantity { get { return 0; } }
        public double Price { get { return 0; } }
        public DateTime Time { get { return DateTime.MinValue; } }
    }

    public class OrderEventArgs : EventArgs
    {
        public Order Order { get { return null; } }
        public OrderState OrderState { get { return OrderState.Unknown; } }
        public string OrderId { get { return ""; } }
        public int Quantity { get { return 0; } }
        public double LimitPrice { get { return 0; } }
        public DateTime Time { get { return DateTime.MinValue; } }
        public string Error { get { return ""; } }
        public string NativeError { get { return ""; } }
    }

    public class PositionEventArgs : EventArgs
    {
        public Position Position { get { return null; } }
        public Instrument Instrument { get { return null; } }
        public MarketPosition MarketPosition { get { return MarketPosition.Flat; } }
        public int Quantity { get { return 0; } }
        public double AveragePrice { get { return 0; } }
    }

    public class AccountItemEventArgs : EventArgs
    {
        public Account Account { get { return null; } }
        public AccountItem AccountItem { get { return AccountItem.CashValue; } }
        public Currency Currency { get { return Currency.UsDollar; } }
        public double Value { get { return 0; } }
    }

    public class ConnectionStatusEventArgs : EventArgs
    {
        public ConnectionStatus Status { get { return ConnectionStatus.Disconnected; } }
        public ConnectionStatus PreviousStatus { get { return ConnectionStatus.Disconnected; } }
    }

    public class Connection
    {
        public string Name { get; set; }
        public ConnectionStatus Status { get; set; }
        public static System.Collections.Generic.List<Connection> Connections { get { return null; } }
    }

    /// <summary>
    /// A live trading account. Account.All is the platform's master list and is
    /// the object NinjaTrader documents locking before enumeration.
    /// </summary>
    public class Account
    {
        /// <summary>Every account the platform knows about. Lock before enumerating.</summary>
        public static System.Collections.Generic.List<Account> All { get { return null; } }

        public string Name { get; set; }
        public string DisplayName { get { return Name; } }
        public AccountType AccountType { get; set; }
        public Connection Connection { get { return null; } }
        public ConnectionStatus ConnectionStatus { get { return ConnectionStatus.Disconnected; } }
        public Currency Denomination { get; set; }
        public System.Collections.Generic.List<Position> Positions { get { return null; } }
        public System.Collections.Generic.List<Order> Orders { get { return null; } }
        public System.Collections.Generic.List<Execution> Executions { get { return null; } }
        public TradeCollection TradesPerformance { get { return null; } }

        public double Get(AccountItem accountItem, Currency currency) { return 0; }
        public void Flatten(System.Collections.Generic.IEnumerable<Instrument> instruments) { }
        public void CancelAllOrders(Instrument instrument) { }
        public Position GetPosition(Instrument instrument) { return null; }

        public event EventHandler<AccountItemEventArgs> AccountItemUpdate;
        public event EventHandler<ExecutionEventArgs> ExecutionUpdate;
        public event EventHandler<OrderEventArgs> OrderUpdate;
        public event EventHandler<PositionEventArgs> PositionUpdate;

        private void Silence()
        {
            if (AccountItemUpdate != null || ExecutionUpdate != null
                || OrderUpdate != null || PositionUpdate != null) { }
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.Data
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.Data
{
    using NinjaTrader.Cbi;

    public enum BarsPeriodType { Tick, Volume, Range, Second, Minute, Day, Week, Month, Year, Renko, LineBreak, Kagi, PointAndFigure, HeikenAshi, Final }
    public enum MarketDataType { Ask, Bid, Last, DailyHigh, DailyLow, DailyVolume, LastClose, Opening, OpenInterest, Settlement, Unknown }
    public enum TradingHoursBreakLineType { Line, Gap, None }

    public class BarsPeriod
    {
        public BarsPeriodType BarsPeriodType { get; set; }
        public int Value { get; set; }
        public int Value2 { get; set; }
        public int BaseBarsPeriodValue { get; set; }
        public override string ToString() { return ""; }
    }

    public class TradingHours
    {
        public string Name { get; set; }
        public string TimeZone { get; set; }
    }

    public class SessionIterator
    {
        public SessionIterator(Bars bars) { }
        public DateTime ActualSessionBegin { get { return DateTime.MinValue; } }
        public DateTime ActualSessionEnd { get { return DateTime.MinValue; } }
        public DateTime GetTradingDay(DateTime time) { return time; }
        public bool IsNewSession(DateTime time, bool isBar) { return false; }
        public void GetNextSession(DateTime time, bool isBar) { }
    }

    public class Bars
    {
        public Instrument Instrument { get; set; }
        public BarsPeriod BarsPeriod { get; set; }
        public TradingHours TradingHours { get; set; }
        public int Count { get { return 0; } }
        public double GetOpen(int barIndex) { return 0; }
        public double GetHigh(int barIndex) { return 0; }
        public double GetLow(int barIndex) { return 0; }
        public double GetClose(int barIndex) { return 0; }
        public DateTime GetTime(int barIndex) { return DateTime.MinValue; }
        public long GetVolume(int barIndex) { return 0; }
    }

    public class MarketDataEventArgs : EventArgs
    {
        public MarketDataType MarketDataType { get { return MarketDataType.Unknown; } }
        public double Price { get { return 0; } }
        public long Volume { get { return 0; } }
        public DateTime Time { get { return DateTime.MinValue; } }
        public Instrument Instrument { get { return null; } }
    }

    public class MarketDepthEventArgs : EventArgs { }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.Gui   (NTWindow, NTMenuItem, ControlCenter, chart plumbing)
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.Gui
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;

    /// <summary>
    /// NinjaTrader's own Window subclass. Every add-on window derives from it so
    /// it picks up the platform's skin, docking and workspace persistence.
    /// </summary>
    public class NTWindow : Window
    {
        public NTWindow() { }
        public string Caption { get; set; }
        public bool IsShownInTaskbar { get; set; }
        public bool CanClose { get; set; }
        public string WorkspaceOptionsName { get; set; }
        public object MainTabControl { get; set; }
        public UIElement FindFirst(string automationId) { return null; }
        public System.Collections.Generic.List<UIElement> FindAll(string automationId) { return null; }
        protected virtual void OnRestoreWindow() { }
        protected virtual void OnSaveWindow() { }
    }

    /// <summary>The Control Center window itself.</summary>
    public class ControlCenter : NTWindow { }

    public class NTMenuItem : MenuItem
    {
        public NTMenuItem() { }
    }

    public class NTTabPage : ContentControl
    {
        public string TabName { get; set; }
    }

    public class NTTabControl : TabControl { }

    public class NTButton : Button { }
    public class NTCheckBox : CheckBox { }
    public class NTComboBox : ComboBox { }
    public class NTTextBox : TextBox { }
    public class NTGrid : Grid { }

    public interface INTTabFactory
    {
        NTWindow CreateParentWindow();
        NTTabPage CreateTabPage(string typeName, bool isTrue);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.Gui.Tools   (SimpleFont, TextPosition, dialogs)
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.Gui.Tools
{
    using System.Windows;
    using System.Windows.Media;
    using System.Xml.Linq;

    /// <summary>
    /// How a window identifies itself inside a saved workspace. A window that
    /// has one is written to the workspace file and reopened with it; a window
    /// that does not is simply gone the next time NinjaTrader starts.
    /// </summary>
    public class WorkspaceOptions
    {
        public WorkspaceOptions(string name, NTWindow window) { Name = name; Window = window; }
        public string Name { get; set; }
        public NTWindow Window { get; set; }
    }

    /// <summary>Implemented by any AddOn window that should survive a restart.</summary>
    public interface IWorkspacePersistence
    {
        WorkspaceOptions WorkspaceOptions { get; set; }
        void Restore(XDocument document, XElement element);
        void Save(XDocument document, XElement element);
    }

    /// <summary>
    /// NinjaTrader's serialisable font. Drawing tools take one of these rather
    /// than a WPF Typeface so the setting survives a workspace round-trip.
    /// </summary>
    public class SimpleFont
    {
        public SimpleFont() { }
        public SimpleFont(string family, double size) { Family = family; Size = size; }

        public string Family { get; set; }
        public double Size { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }

        public Typeface ToTypeface() { return null; }
        public FontFamily ToFontFamily() { return null; }
        public override string ToString() { return ""; }
    }

    /// <summary>Where a fixed-position text object sits on the chart panel.</summary>
    public enum TextPosition { TopLeft, TopRight, BottomLeft, BottomRight, Center }

    public static class NTMessageBoxSimple
    {
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage image) { return MessageBoxResult.None; }
        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button, MessageBoxImage image) { return MessageBoxResult.None; }
    }

    public class NTMessageBox
    {
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage image) { return MessageBoxResult.None; }
    }

    public class Stroke
    {
        public Stroke() { }
        public Stroke(Brush brush) { }
        public Stroke(Brush brush, double width) { }
        public Brush Brush { get; set; }
        public double Width { get; set; }
        public int Opacity { get; set; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.Gui.Chart
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.Gui.Chart
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using NinjaTrader.Cbi;
    using NinjaTrader.Data;

    public enum ChartPhase { Undefined, Normal, Rendering }

    public class ChartControl : Control
    {
        public Instrument Instrument { get; set; }
        public BarsPeriod BarsPeriod { get; set; }
        public ChartPanel ChartPanel { get; set; }
        public System.Collections.Generic.List<ChartPanel> ChartPanels { get { return null; } }
        public ChartProperties Properties { get { return null; } }
        public double CanvasLeft { get { return 0; } }
        public double CanvasRight { get { return 0; } }
        public int BarWidth { get { return 1; } }
        public int BarSpace { get { return 1; } }
        public int FirstTimePainted { get { return 0; } }
        public int LastTimePainted { get { return 0; } }
        public int GetSlotIndexByTime(DateTime time) { return 0; }
        public double GetXByBarIndex(ChartBars chartBars, int barIndex) { return 0; }
        public double GetXByTime(DateTime time) { return 0; }
        public void InvalidateVisualNow() { }
    }

    public class ChartProperties
    {
        public Brush ChartBackground { get; set; }
        public Brush ChartText { get; set; }
        public Brush AxisPen { get; set; }
        public SimpleFontHolder LabelFont { get; set; }
    }

    public class SimpleFontHolder { }

    public class ChartPanel : Control
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public double MaxValue { get; set; }
        public double MinValue { get; set; }
        public int PanelIndex { get { return 0; } }
    }

    public class ChartBars
    {
        public Bars Bars { get; set; }
        public int FromIndex { get; set; }
        public int ToIndex { get; set; }
        public int Count { get { return 0; } }
        public DateTime GetTimeByBarIdx(ChartControl chartControl, int barIndex) { return DateTime.MinValue; }
    }

    public class ChartScale
    {
        public double MaxValue { get; set; }
        public double MinValue { get; set; }
        public int GetYByValue(double value) { return 0; }
        public double GetValueByY(double y) { return 0; }
    }

    public class ChartWindow : NinjaTrader.Gui.NTWindow
    {
        public ChartControl ActiveChartControl { get; set; }
        public ChartControl SelectedChartControl { get; set; }
        public System.Collections.Generic.List<ChartControl> ChartControls { get { return null; } }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.Gui.NinjaScript  (render/chart base classes under NinjaScript)
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.Gui.NinjaScript
{
    using NinjaTrader.Gui.Chart;
    using NinjaTrader.NinjaScript;

    /// <summary>
    /// Everything that can draw on a chart hangs off this. Ballast's indicator
    /// only ever touches ChartControl and ForceRefresh.
    /// </summary>
    public abstract class ChartRenderBase : NinjaScriptBase
    {
        public ChartControl ChartControl { get { return null; } }
        public ChartBars ChartBars { get { return null; } }
        public ChartPanel ChartPanel { get { return null; } }
        public ChartScale ChartScale { get { return null; } }
        public bool IsVisible { get; set; }
        public bool DrawOnPricePanel { get; set; }
        public bool DisplayInDataBox { get; set; }
        public bool PaintPriceMarkers { get; set; }
        public bool IsOverlay { get; set; }
        public bool IsSuspendedWhileInactive { get; set; }
        public bool IsAutoScale { get; set; }
        public string ChartOnly { get; set; }

        /// <summary>Ask the chart to repaint. Safe from a timer tick.</summary>
        public void ForceRefresh() { }
        public void RemoveDrawObject(string tag) { }
        public void RemoveDrawObjects() { }
        protected virtual void OnRender(ChartControl chartControl, ChartScale chartScale) { }
        protected virtual void OnRenderTargetChanged() { }
    }

    public abstract class IndicatorRenderBase : ChartRenderBase { }
    public abstract class StrategyRenderBase : ChartRenderBase { }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.NinjaScript  (base classes, State machine, AddOnBase)
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript
{
    using System.Windows;
    using System.Windows.Media;
    using NinjaTrader.Cbi;
    using NinjaTrader.Data;

    public enum State { SetDefaults, Configure, DataLoaded, Historical, Transition, Realtime, Terminated, Finalized, Active }

    public enum Calculate { OnBarClose, OnEachTick, OnPriceChange }

    public enum MaximumBarsLookBack { TwoHundredFiftySix, Infinite }

    public enum StartBehavior { WaitUntilFlat, WaitUntilFlatSynchronizeAccount, AdoptAccountPosition, ImmediatelySubmit, ImmediatelySubmitSynchronizeAccount }

    public enum ConnectionLossHandling { KeepRunning, Recalculate, StopStrategy }

    public enum ErrorHandling { StopCancelClose, StopCancelCloseIgnoreRejects, HandleErrorsManually, CatchHandleErrors }

    public enum NinjaScriptType { Indicator, Strategy, AddOn, DrawingTool, ShareService, SuperDom, Optimizer }

    /// <summary>
    /// Root of every NinjaScript object. The platform drives it through
    /// OnStateChange; everything else is optional.
    /// </summary>
    public abstract class NinjaScriptBase
    {
        /// <summary>
        /// Marshals a callback onto the NinjaScript thread. Real signature takes
        /// an Action&lt;object&gt; and a state object.
        /// </summary>
        public void TriggerCustomEvent(Action<object> callback, object state) { }
        public void TriggerCustomEvent(Action<object> callback, int priority, object state) { }

        public State State { get; set; }
        public string Name { get; set; }
        /// <summary>Label NinjaTrader prints on the chart panel for this instance.</summary>
        public virtual string DisplayName { get { return Name; } }
        public string Description { get; set; }
        public Calculate Calculate { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsInStrategyAnalyzer { get { return false; } }
        public int BarsInProgress { get; set; }
        public int CurrentBar { get { return 0; } }
        public Bars Bars { get { return null; } }
        public BarsPeriod BarsPeriod { get { return null; } }
        public Instrument Instrument { get { return null; } }
        public DateTime Time0 { get { return DateTime.MinValue; } }
        public double Close0 { get { return 0; } }
        public MaximumBarsLookBack MaximumBarsLookBack { get; set; }
        public System.Windows.Threading.Dispatcher Dispatcher { get { return null; } }

        protected virtual void OnStateChange() { }
        protected virtual void OnBarUpdate() { }
        protected virtual void OnMarketData(MarketDataEventArgs marketDataUpdate) { }
        protected virtual void OnMarketDepth(MarketDepthEventArgs marketDepthUpdate) { }
        protected virtual void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate) { }
        protected virtual void OnFundamentalData(EventArgs e) { }

        public void Print(object value) { }
        public void ClearOutputWindow() { }
        public void Log(string message, LogLevel logLevel) { }
        public void AddDataSeries(BarsPeriodType periodType, int period) { }
        public void AddDataSeries(string instrumentName, BarsPeriodType periodType, int period) { }
    }

    public enum LogLevel { Information, Warning, Error, Alert }

    /// <summary>
    /// Base for add-ons. NinjaTrader calls OnWindowCreated for every window it
    /// opens, which is how Ballast finds the Control Center's New menu.
    /// </summary>
    public abstract class AddOnBase : NinjaScriptBase
    {
        protected virtual void OnWindowCreated(Window window) { }
        protected virtual void OnWindowDestroyed(Window window) { }
    }

    public class SeriesDouble
    {
        public double this[int barsAgo] { get { return 0; } set { } }
    }

    public class SeriesCollection
    {
        public SeriesDouble this[int index] { get { return new SeriesDouble(); } }
    }

    public abstract class IndicatorBase : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        public bool BarsRequiredToPlot { get; set; }
        public System.Collections.Generic.List<object> Plots { get { return null; } }
        public System.Collections.Generic.List<object> Lines { get { return null; } }
        public void AddPlot(Brush brush, string name) { }
        public void AddLine(Brush brush, double value, string name) { }
        public SeriesCollection Values { get { return null; } }
    }

    public abstract class StrategyBase : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Account Account { get { return null; } }
        public MarketPosition PositionMarketPosition { get { return MarketPosition.Flat; } }
    }

    public abstract class DrawingToolBase : NinjaTrader.Gui.NinjaScript.ChartRenderBase { }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.NinjaScript.Indicators
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// The class user indicators derive from. Sits on top of the chart render
    /// base, which is where ChartControl / ForceRefresh / RemoveDrawObject live.
    /// </summary>
    public abstract class Indicator : NinjaTrader.NinjaScript.IndicatorBase { }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.NinjaScript.Strategies
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Strategies
{
    public abstract class Strategy : NinjaTrader.NinjaScript.StrategyBase { }
}

// ═════════════════════════════════════════════════════════════════════════════
// NinjaTrader.NinjaScript.DrawingTools
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.DrawingTools
{
    using System.Windows.Media;
    using NinjaTrader.Gui.Tools;
    using NinjaTrader.NinjaScript;

    public class DrawingTool : DrawingToolBase
    {
        public string Tag { get; set; }
        public bool IsLocked { get; set; }
        public bool IsUserDrawn { get; set; }
    }

    public class Text : DrawingTool
    {
        public string DisplayText { get; set; }
        public Brush TextBrush { get; set; }
        public SimpleFont Font { get; set; }
    }

    public class TextFixed : DrawingTool
    {
        public string DisplayText { get; set; }
        public TextPosition TextPosition { get; set; }
        public Brush TextBrush { get; set; }
        public Brush AreaBrush { get; set; }
        public Brush OutlineBrush { get; set; }
        public int AreaOpacity { get; set; }
        public SimpleFont Font { get; set; }
    }

    public class Line : DrawingTool { }
    public class Ray : DrawingTool { }
    public class Rectangle : DrawingTool { }
    public class Dot : DrawingTool { }
    public class ArrowUp : DrawingTool { }
    public class ArrowDown : DrawingTool { }

    /// <summary>
    /// The static drawing façade. Ballast only calls TextFixed, but the whole
    /// family is listed the way NinjaTrader exposes it.
    /// </summary>
    public static class Draw
    {
        public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition) { return null; }

        public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition,
                                          Brush textBrush, SimpleFont font, Brush outlineBrush, Brush areaBrush, int areaOpacity) { return null; }

        public static Text Text(NinjaScriptBase owner, string tag, string text, int barsAgo, double y) { return null; }
        public static Text Text(NinjaScriptBase owner, string tag, string text, DateTime time, double y) { return null; }

        public static Line Line(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush) { return null; }
        public static Ray Ray(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush) { return null; }
        public static Rectangle Rectangle(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush) { return null; }
        public static Dot Dot(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush) { return null; }
        public static ArrowUp ArrowUp(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush) { return null; }
        public static ArrowDown ArrowDown(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush) { return null; }
    }
}
