using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Plugview.Windows.Graphics;
using Plugview.Windows.Ui;
using Windows.Win32.Foundation;

namespace Plugview.Windows.App;

// UI Automation, the interfaces Narrator talks to (UIAutomationCore.h), declared for source-generated COM so NativeAOT
// can expose them. Only what plugview's flat window of controls needs.

/// <summary>A rectangle in screen pixels.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    int GetProviderOptions();

    [return: MarshalAs(UnmanagedType.Interface)]
    object? GetPatternProvider(int patternId);

    /// <summary>Writes the property's VARIANT (VT_EMPTY when the element does not have it).</summary>
    [PreserveSig]
    unsafe int GetPropertyValue(int propertyId, ComVariant* value);

    /// <summary>The window's own provider for the root; nothing (0) for the controls inside it.</summary>
    nint GetHostRawElementProvider();
}

[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    IRawElementProviderFragment? Navigate(int direction);

    /// <summary>A SAFEARRAY of VT_I4.</summary>
    nint GetRuntimeId();

    UiaRect GetBoundingRectangle();

    nint GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot GetFragmentRoot();
}

[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
}

[GeneratedComInterface]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IInvokeProvider
{
    void Invoke();
}

[GeneratedComInterface]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IRangeValueProvider
{
    void SetValue(double value);

    double GetValue();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetIsReadOnly();

    double GetMaximum();

    double GetMinimum();

    double GetLargeChange();

    double GetSmallChange();
}

/// <summary>UI Automation's numbers: properties, control types, patterns, events.</summary>
internal static class Uia
{
    public const int InvokePattern = 10000;
    public const int RangeValuePattern = 10003;

    public const int ControlTypeProperty = 30003;
    public const int NameProperty = 30005;
    public const int HasKeyboardFocusProperty = 30008;
    public const int IsKeyboardFocusableProperty = 30009;
    public const int IsEnabledProperty = 30010;
    public const int AutomationIdProperty = 30011;
    public const int IsControlElementProperty = 30016;
    public const int IsContentElementProperty = 30017;
    public const int ProviderDescriptionProperty = 30107;

    public const int ButtonControl = 50000;
    public const int CheckBoxControl = 50002;
    public const int SliderControl = 50015;
    public const int TextControl = 50020;
    public const int SplitButtonControl = 50031;
    public const int PaneControl = 50033;

    public const int AutomationFocusChangedEvent = 20005;

    /// <summary>ProviderOptions_ServerSideProvider | ProviderOptions_UseComThreading.</summary>
    public const int ServerSideComThreaded = 0x1 | 0x20;

    /// <summary>The prefix UI Automation asks child runtime IDs to start with.</summary>
    public const int AppendRuntimeId = 3;

    public const int NavigateParent = 0, NavigateNextSibling = 1, NavigatePreviousSibling = 2, NavigateFirstChild = 3, NavigateLastChild = 4;
}

/// <summary>The native side of UI Automation and SAFEARRAYs.</summary>
internal static partial class UiaNative
{
    [LibraryImport("UIAutomationCore.dll")]
    public static partial nint UiaReturnRawElementProvider(nint window, nuint wParam, nint lParam, nint provider);

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaHostProviderFromHwnd(nint window, out nint provider);

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaRaiseAutomationEvent(nint provider, int eventId);

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaRaiseStructureChangedEvent(nint provider, int structureChangeType, nint runtimeId, int runtimeIdLength);

    [LibraryImport("UIAutomationCore.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int UiaRaiseNotificationEvent(nint provider, int notificationKind, int notificationProcessing, [MarshalAs(UnmanagedType.BStr)] string displayString, [MarshalAs(UnmanagedType.BStr)] string activityId);

    [LibraryImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UiaClientsAreListening();

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaDisconnectProvider(nint provider);

    [LibraryImport("oleaut32.dll")]
    public static partial nint SafeArrayCreateVector(ushort variantType, int lowerBound, uint count);

    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayPutElement(nint array, ref int index, ref int value);

    /// <summary>A SAFEARRAY of VT_I4 holding <paramref name="values"/>.</summary>
    public static nint IntArray(ReadOnlySpan<int> values)
    {
        const ushort variantInt32 = 3;
        var array = SafeArrayCreateVector(variantInt32, 0, (uint)values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            SafeArrayPutElement(array, ref index, ref value);
        }
        return array;
    }
}

/// <summary>
/// Narrator's view of the window: the window itself is the root (a pane named for what it shows), and every element
/// of the last drawn frame (<see cref="UiFrame"/>) is a child with its name, control type, place and actions.
/// </summary>
[GeneratedComClass]
internal sealed unsafe partial class AccessibilityProvider : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot, IDisposable
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private readonly AppWindow window;
    private readonly Dictionary<string, ElementProvider> elements = [];
    private readonly Dictionary<string, int> runtimeIds = [];
    private int nextRuntimeId = 1;
    private nint rootPointer;

    public AccessibilityProvider(AppWindow window)
    {
        this.window = window;
        window.AccessibilityRequested = Answer;
        window.FocusChanged += element =>
        {
            if (element is not null && UiaNative.UiaClientsAreListening()) RaiseFocus(Element(element));
        };
    }

    /// <summary>WM_GETOBJECT: hands UI Automation the root provider.</summary>
    private LRESULT? Answer(WPARAM wParam, LPARAM lParam)
    {
        // UiaRootObjectId; other ids (MSAA) get Windows' default answer.
        if ((int)lParam.Value != -25) return null;
        if (rootPointer == 0) rootPointer = Pointer(this, typeof(IRawElementProviderSimple).GUID);
        return new LRESULT(UiaNative.UiaReturnRawElementProvider((nint)window.Handle.Value, wParam.Value, lParam.Value, rootPointer));
    }

    /// <summary>The window's controls changed (a new screen, a notice): Narrator re-reads them.</summary>
    public void StructureChanged()
    {
        if (!UiaNative.UiaClientsAreListening()) return;
        // StructureChangeType_ChildrenInvalidated on the root; Narrator reads the new frame's elements.
        var root = Pointer(this, typeof(IRawElementProviderSimple).GUID);
        try
        {
            _ = UiaNative.UiaRaiseStructureChangedEvent(root, 1, 0, 0);
        }
        finally
        {
            Marshal.Release(root);
        }
    }

    /// <summary>Narrator says <paramref name="message"/> without moving focus (a toast, a notice).</summary>
    public void Announce(string message)
    {
        if (!UiaNative.UiaClientsAreListening()) return;
        var root = Pointer(this, typeof(IRawElementProviderSimple).GUID);
        try
        {
            // NotificationKind_Other, NotificationProcessing_ImportantMostRecent.
            _ = UiaNative.UiaRaiseNotificationEvent(root, 4, 1, message, "plugview.notice");
        }
        finally
        {
            Marshal.Release(root);
        }
    }

    private ElementProvider Element(UiElement element)
    {
        if (!runtimeIds.TryGetValue(element.Id, out var runtimeId))
        {
            runtimeId = nextRuntimeId++;
            runtimeIds[element.Id] = runtimeId;
        }
        if (elements.TryGetValue(element.Id, out var existing) && existing.Element == element) return existing;
        var provider = new ElementProvider(this, element, runtimeId);
        elements[element.Id] = provider;
        return provider;
    }

    private static void RaiseFocus(ElementProvider provider)
    {
        var pointer = Pointer(provider, typeof(IRawElementProviderSimple).GUID);
        try
        {
            _ = UiaNative.UiaRaiseAutomationEvent(pointer, Uia.AutomationFocusChangedEvent);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static nint Pointer(object provider, Guid interfaceId)
    {
        var unknown = ComWrappers.GetOrCreateComInterfaceForObject(provider, CreateComInterfaceFlags.None);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in interfaceId, out var pointer));
            return pointer;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>The elements Narrator lists: what the last frame drew, in reading order.</summary>
    private IReadOnlyList<UiElement> Children => window.Frame.Elements;

    internal IRawElementProviderFragment? Sibling(UiElement element, int step)
    {
        var children = Children;
        var index = children.ToList().FindIndex(child => child.Id == element.Id);
        var next = index + step;
        return index < 0 || next < 0 || next >= children.Count ? null : Element(children[next]);
    }

    internal UiaRect ScreenRect(Box box)
    {
        var point = new System.Drawing.Point((int)(box.X * window.Scale), (int)(box.Y * window.Scale));
        global::Windows.Win32.PInvoke.ClientToScreen(window.Handle, ref point);
        return new UiaRect { Left = point.X, Top = point.Y, Width = box.Width * window.Scale, Height = box.Height * window.Scale };
    }

    internal bool HasFocus(UiElement element) => window.Interaction.FocusedId == element.Id;

    internal void Focus(UiElement element)
    {
        window.Interaction.FocusedId = element.Id;
        window.Interaction.ShowsFocus = true;
        window.Invalidate();
    }

    // MARK: - The root (the window)

    public int GetProviderOptions() => Uia.ServerSideComThreaded;

    public object? GetPatternProvider(int patternId) => null;

    public unsafe int GetPropertyValue(int propertyId, ComVariant* value)
    {
        *value = propertyId switch
        {
            Uia.ControlTypeProperty => ComVariant.Create(Uia.PaneControl),
            Uia.NameProperty => ComVariant.Create("plugview"),
            Uia.ProviderDescriptionProperty => ComVariant.Create("plugview"),
            _ => default,
        };
        return 0;
    }

    public nint GetHostRawElementProvider()
    {
        UiaNative.UiaHostProviderFromHwnd((nint)window.Handle.Value, out var host);
        return host;
    }

    public IRawElementProviderFragment? Navigate(int direction)
    {
        var children = Children;
        if (children.Count == 0) return null;
        return direction switch
        {
            Uia.NavigateFirstChild => Element(children[0]),
            Uia.NavigateLastChild => Element(children[^1]),
            _ => null,
        };
    }

    // The root's runtime ID comes from the window's host provider.
    public nint GetRuntimeId() => 0;

    public UiaRect GetBoundingRectangle() => default;

    public nint GetEmbeddedFragmentRoots() => 0;

    public void SetFocus()
    {
    }

    public IRawElementProviderFragmentRoot GetFragmentRoot() => this;

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        var point = new System.Drawing.Point((int)x, (int)y);
        global::Windows.Win32.PInvoke.ScreenToClient(window.Handle, ref point);
        var dipX = point.X / window.Scale;
        var dipY = point.Y / window.Scale;
        for (var index = Children.Count - 1; index >= 0; index--)
        {
            if (Children[index].Bounds.Contains(dipX, dipY)) return Element(Children[index]);
        }
        return this;
    }

    public IRawElementProviderFragment? GetFocus() =>
        window.Frame.Find(window.Interaction.FocusedId) is { } focused ? Element(focused) : null;

    public void Dispose()
    {
        window.AccessibilityRequested = null;
        if (rootPointer != 0)
        {
            _ = UiaNative.UiaDisconnectProvider(rootPointer);
            Marshal.Release(rootPointer);
            rootPointer = 0;
        }
    }
}

/// <summary>One control or text in the window, as Narrator sees it.</summary>
[GeneratedComClass]
internal sealed unsafe partial class ElementProvider(AccessibilityProvider root, UiElement element, int runtimeId)
    : IRawElementProviderSimple, IRawElementProviderFragment, IInvokeProvider, IRangeValueProvider
{
    public UiElement Element { get; } = element;

    public int GetProviderOptions() => Uia.ServerSideComThreaded;

    public object? GetPatternProvider(int patternId) => patternId switch
    {
        Uia.InvokePattern when Element.Invoke is not null && Element.Role != UiRole.Slider => this,
        Uia.RangeValuePattern when Element.Role == UiRole.Slider => this,
        _ => null,
    };

    public unsafe int GetPropertyValue(int propertyId, ComVariant* value)
    {
        *value = Property(propertyId);
        return 0;
    }

    private ComVariant Property(int propertyId) => propertyId switch
    {
        Uia.ControlTypeProperty => ComVariant.Create(Element.Role switch
        {
            UiRole.Button => Uia.ButtonControl,
            UiRole.Text => Uia.TextControl,
            UiRole.Slider => Uia.SliderControl,
            UiRole.CheckBox => Uia.CheckBoxControl,
            UiRole.MenuButton => Uia.SplitButtonControl,
            _ => throw new ArgumentOutOfRangeException(nameof(propertyId), Element.Role, "Unhandled role"),
        }),
        Uia.NameProperty => ComVariant.Create(Element.Name),
        Uia.AutomationIdProperty => ComVariant.Create(Element.Id),
        Uia.IsKeyboardFocusableProperty => ComVariant.Create(Element.IsInteractive),
        Uia.HasKeyboardFocusProperty => ComVariant.Create(root.HasFocus(Element)),
        Uia.IsEnabledProperty => ComVariant.Create(true),
        Uia.IsControlElementProperty => ComVariant.Create(true),
        Uia.IsContentElementProperty => ComVariant.Create(true),
        _ => default,
    };

    public nint GetHostRawElementProvider() => 0;

    public IRawElementProviderFragment? Navigate(int direction) => direction switch
    {
        Uia.NavigateParent => root,
        Uia.NavigateNextSibling => root.Sibling(Element, 1),
        Uia.NavigatePreviousSibling => root.Sibling(Element, -1),
        _ => null,
    };

    public nint GetRuntimeId() => UiaNative.IntArray([Uia.AppendRuntimeId, runtimeId]);

    public UiaRect GetBoundingRectangle() => root.ScreenRect(Element.Bounds);

    public nint GetEmbeddedFragmentRoots() => 0;

    public void SetFocus() => root.Focus(Element);

    public IRawElementProviderFragmentRoot GetFragmentRoot() => root;

    public void Invoke() => Element.Invoke?.Invoke();

    public void SetValue(double value)
    {
        // The slider's Drag takes a pointer position; map the value onto its track (6 DIPs in from each end).
        var track = Element.Bounds;
        Element.Drag?.Invoke(track.X + 6 + ((float)(value / 100) * (track.Width - 12)));
    }

    public double GetValue() => Element.Value ?? 0;

    public bool GetIsReadOnly() => false;

    public double GetMaximum() => 100;

    public double GetMinimum() => 0;

    public double GetLargeChange() => 10;

    public double GetSmallChange() => 2;
}
