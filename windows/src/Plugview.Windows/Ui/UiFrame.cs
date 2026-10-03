using Plugview.Windows.Graphics;

namespace Plugview.Windows.Ui;

/// <summary>What an element is, for Narrator (UI Automation control types) and for keyboard focus.</summary>
internal enum UiRole
{
    Button,
    Text,
    Slider,
    CheckBox,
    MenuButton,
}

/// <summary>Something on screen: its place, what Narrator calls it, and what clicking it does.</summary>
/// <param name="Id">Stable across frames, so hover, focus and Narrator follow the element as it redraws.</param>
/// <param name="Bounds">Where it is, in DIPs from the window's top-left corner.</param>
/// <param name="Role">Its control type.</param>
/// <param name="Name">The words Narrator reads, and the tooltip.</param>
/// <param name="Invoke">What a click, Enter or Space does; <c>null</c> for text.</param>
/// <param name="Value">A slider's value 0…100, a check box's state as 0 or 1.</param>
/// <param name="Drag">A slider: called with the pointer's x (DIPs) when pressed and while dragged.</param>
internal sealed record UiElement(string Id, Box Bounds, UiRole Role, string Name, Action? Invoke = null, double? Value = null, Action<float>? Drag = null)
{
    public bool IsInteractive => Invoke is not null || Drag is not null;
}

/// <summary>Where the pointer and the keyboard are, so widgets draw their hover, pressed and focus states.</summary>
internal sealed class Interaction
{
    public string? HoveredId { get; set; }

    public string? PressedId { get; set; }

    /// <summary>The element keyboard focus is on (Tab, Shift+Tab); shown with a focus ring.</summary>
    public string? FocusedId { get; set; }

    /// <summary>True once Tab was used: focus rings show only for keyboard users, as in Windows.</summary>
    public bool ShowsFocus { get; set; }

    public bool IsHovered(string id) => HoveredId == id;

    public bool IsPressed(string id) => PressedId == id && HoveredId == id;

    public bool IsFocused(string id) => ShowsFocus && FocusedId == id;
}

/// <summary>
/// The elements one drawn frame put on screen, in drawing order. Clicks, keyboard focus and Narrator all read this
/// list, so they always match what is visible.
/// </summary>
internal sealed class UiFrame
{
    private readonly List<UiElement> elements = [];

    public IReadOnlyList<UiElement> Elements => elements;

    public void Add(UiElement element) => elements.Add(element);

    /// <summary>The topmost interactive element under (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public UiElement? HitTest(float x, float y)
    {
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            if (elements[index].IsInteractive && elements[index].Bounds.Contains(x, y)) return elements[index];
        }
        return null;
    }

    public UiElement? Find(string? id) => id is null ? null : elements.FirstOrDefault(element => element.Id == id);

    /// <summary>The next interactive element after <paramref name="currentId"/> (Tab), or before it (Shift+Tab), wrapping.</summary>
    public UiElement? NextFocus(string? currentId, bool isBackward)
    {
        var focusable = elements.Where(element => element.IsInteractive).ToList();
        if (focusable.Count == 0) return null;
        var index = focusable.FindIndex(element => element.Id == currentId);
        if (index < 0) return isBackward ? focusable[^1] : focusable[0];
        var next = (index + (isBackward ? -1 : 1) + focusable.Count) % focusable.Count;
        return focusable[next];
    }
}
