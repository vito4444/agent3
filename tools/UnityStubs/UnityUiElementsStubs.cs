// UI Toolkit surface stubs (see UnityEngineStubs.cs header). Style properties accept
// the same implicit conversions the real StyleXXX wrappers do.
// ReSharper disable all
#pragma warning disable
using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    public enum DisplayStyle { Flex, None }
    public enum Position { Relative, Absolute }
    public enum FlexDirection { Column, ColumnReverse, Row, RowReverse }
    public enum Wrap { NoWrap, Wrap, WrapReverse }
    public enum Align { Auto, FlexStart, Center, FlexEnd, Stretch }
    public enum Justify { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround }
    public enum WhiteSpace { Normal, NoWrap }

    public struct Length
    {
        public static Length Percent(float value) => new Length();
    }

    public struct StyleLength
    {
        public static implicit operator StyleLength(float v) => new StyleLength();
        public static implicit operator StyleLength(int v) => new StyleLength();
        public static implicit operator StyleLength(Length v) => new StyleLength();
    }

    public struct StyleFloat
    {
        public static implicit operator StyleFloat(float v) => new StyleFloat();
        public static implicit operator StyleFloat(int v) => new StyleFloat();
    }

    public struct StyleColor
    {
        public static implicit operator StyleColor(Color c) => new StyleColor();
    }

    public struct StyleEnum<T> where T : struct, IConvertible
    {
        private T _value;
        public T value => _value;
        public static implicit operator StyleEnum<T>(T v) => new StyleEnum<T> { _value = v };
        public static bool operator ==(StyleEnum<T> a, T b) => EqualityComparer<T>.Default.Equals(a._value, b);
        public static bool operator !=(StyleEnum<T> a, T b) => !EqualityComparer<T>.Default.Equals(a._value, b);
        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => 0;
    }

    public class IStyle
    {
        public StyleLength top { get; set; }
        public StyleLength bottom { get; set; }
        public StyleLength left { get; set; }
        public StyleLength right { get; set; }
        public StyleLength width { get; set; }
        public StyleLength height { get; set; }
        public StyleLength maxHeight { get; set; }
        public StyleLength maxWidth { get; set; }
        public StyleLength marginTop { get; set; }
        public StyleLength marginBottom { get; set; }
        public StyleLength marginLeft { get; set; }
        public StyleLength marginRight { get; set; }
        public StyleLength paddingTop { get; set; }
        public StyleLength paddingBottom { get; set; }
        public StyleLength paddingLeft { get; set; }
        public StyleLength paddingRight { get; set; }
        public StyleFloat flexGrow { get; set; }
        public StyleFloat flexShrink { get; set; }
        public StyleFloat fontSize { get; set; }
        public StyleColor backgroundColor { get; set; }
        public StyleColor color { get; set; }
        public StyleEnum<DisplayStyle> display { get; set; }
        public StyleEnum<Position> position { get; set; }
        public StyleEnum<FlexDirection> flexDirection { get; set; }
        public StyleEnum<Wrap> flexWrap { get; set; }
        public StyleEnum<Align> alignItems { get; set; }
        public StyleEnum<Justify> justifyContent { get; set; }
        public StyleEnum<WhiteSpace> whiteSpace { get; set; }
        public StyleEnum<TextAnchor> unityTextAlign { get; set; }
    }

    public class VisualElement
    {
        public IStyle style { get; } = new IStyle();
        public void Add(VisualElement child) { }
        public void Remove(VisualElement child) { }
        public void Clear() { }
    }

    public class TextElement : VisualElement
    {
        public string text { get; set; }
    }

    public class Label : TextElement
    {
        public Label() { }
        public Label(string text) { }
    }

    public class Button : TextElement
    {
        public Button() { }
        public Button(Action clickEvent) { }
        public event Action clicked { add { } remove { } }
    }

    public enum ScrollerVisibility { Auto, AlwaysVisible, Hidden }

    public class ScrollView : VisualElement
    {
        public ScrollerVisibility horizontalScrollerVisibility { get; set; }
        public ScrollerVisibility verticalScrollerVisibility { get; set; }
    }

    public class ChangeEvent<T>
    {
        public T newValue => default;
        public T previousValue => default;
    }

    public delegate void EventCallback<in TEvent>(TEvent evt);

    public interface INotifyValueChanged<T>
    {
        T value { get; set; }
    }

    public class TextField : VisualElement, INotifyValueChanged<string>
    {
        public string value { get; set; }
    }

    public static class INotifyValueChangedExtensions
    {
        public static void RegisterValueChangedCallback<T>(this INotifyValueChanged<T> control,
            EventCallback<ChangeEvent<T>> callback)
        {
        }
    }

    public class PanelSettings : ScriptableObject
    {
    }

    public class UIDocument : MonoBehaviour
    {
        public PanelSettings panelSettings { get; set; }
        public VisualElement rootVisualElement => new VisualElement();
        public float sortingOrder { get; set; }
    }
}
