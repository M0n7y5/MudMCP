// Copyright (c) 2025 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using MudBlazor.Mcp.Services.Parsing;

namespace MudBlazor.Mcp.Tests.Parsing;

public class XmlDocParserTests
{
    private readonly XmlDocParser _parser;

    public XmlDocParserTests()
    {
        var logger = Mock.Of<ILogger<XmlDocParser>>();
        _parser = new XmlDocParser(logger);
    }

    [Fact]
    public void ParseSourceCode_WithValidComponent_ExtractsClassName()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            /// <summary>
            /// A Material Design button component.
            /// </summary>
            public class MudButton : MudBaseButton
            {
                /// <summary>
                /// The color of the button.
                /// </summary>
                [Parameter]
                public Color Color { get; set; } = Color.Default;
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MudButton", result.ClassName);
        Assert.Equal("MudBlazor", result.Namespace);
        Assert.Equal("MudBaseButton", result.BaseType);
    }

    [Fact]
    public void ParseSourceCode_WithXmlDocumentation_ExtractsSummary()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            /// <summary>
            /// A Material Design button component.
            /// </summary>
            /// <remarks>
            /// Use this component for primary user actions.
            /// </remarks>
            public class MudButton : MudBaseButton
            {
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Material Design button", result.Summary);
        Assert.Contains("primary user actions", result.Remarks);
    }

    [Fact]
    public void ParseSourceCode_WithParameters_ExtractsParameters()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public class MudButton : MudBaseButton
            {
                /// <summary>
                /// The color of the button.
                /// </summary>
                [Parameter]
                public Color Color { get; set; } = Color.Default;

                /// <summary>
                /// The size of the button.
                /// </summary>
                [Parameter]
                public Size Size { get; set; }

                /// <summary>
                /// Gets or sets whether the button is disabled.
                /// </summary>
                [Parameter]
                [EditorRequired]
                public bool Disabled { get; set; }
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Parameters.Count);
        
        var colorParam = result.Parameters.First(p => p.Name == "Color");
        Assert.Equal("Color", colorParam.Type);
        Assert.Contains("color of the button", colorParam.Description);
        Assert.Equal("Color.Default", colorParam.DefaultValue);

        var disabledParam = result.Parameters.First(p => p.Name == "Disabled");
        Assert.True(disabledParam.IsRequired);
    }

    [Fact]
    public void ParseSourceCode_WithEvents_ExtractsEventCallbacks()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public class MudButton : MudBaseButton
            {
                /// <summary>
                /// Callback when the button is clicked.
                /// </summary>
                [Parameter]
                public EventCallback<MouseEventArgs> OnClick { get; set; }

                /// <summary>
                /// Callback when the mouse enters the button.
                /// </summary>
                [Parameter]
                public EventCallback OnMouseEnter { get; set; }
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Events.Count);
        
        var onClick = result.Events.First(e => e.Name == "OnClick");
        Assert.Equal("MouseEventArgs", onClick.EventArgsType);

        var onMouseEnter = result.Events.First(e => e.Name == "OnMouseEnter");
        Assert.Null(onMouseEnter.EventArgsType);
    }

    [Fact]
    public void ParseSourceCode_WithPublicMethods_ExtractsMethods()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public class MudButton : MudBaseButton
            {
                /// <summary>
                /// Focuses the button element.
                /// </summary>
                public async Task FocusAsync()
                {
                }

                /// <summary>
                /// Clicks the button programmatically.
                /// </summary>
                public void Click()
                {
                }

                // This should not be included
                private void InternalMethod()
                {
                }
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Methods.Count);
        
        var focusAsync = result.Methods.First(m => m.Name == "FocusAsync");
        Assert.True(focusAsync.IsAsync);
        Assert.Equal("Task", focusAsync.ReturnType);

        var click = result.Methods.First(m => m.Name == "Click");
        Assert.False(click.IsAsync);
        Assert.Equal("void", click.ReturnType);
    }

    [Fact]
    public void ParseSourceCode_WithInternalClass_MarksResultInternal()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            internal class InternalComponent
            {
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "Internal.cs");

        // Assert - internal classes are parsed so IncludeInternalComponents can surface them
        Assert.NotNull(result);
        Assert.Equal("InternalComponent", result.ClassName);
        Assert.True(result.IsInternal);
        Assert.False(result.IsDeprecated);
    }

    [Fact]
    public void ParseSourceCode_WithPublicClass_IsNotInternal()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public class MudButton
            {
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsInternal);
    }

    [Fact]
    public void ParseSourceCode_WithNoClass_ReturnsNull()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public interface INotAComponent
            {
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "INotAComponent.cs");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseSourceCode_WithObsoleteClass_MarksResultDeprecated()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            [Obsolete("Use MudNewButton instead.")]
            public class MudOldButton
            {
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudOldButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsDeprecated);
    }

    [Fact]
    public void ParseSourceCode_EventCallbackParameters_AppearOnlyAsEvents()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public class MudButton
            {
                [Parameter]
                public string Label { get; set; }

                [Parameter]
                public EventCallback OnClosed { get; set; }

                [Parameter]
                public EventCallback<MouseEventArgs> OnClick { get; set; }
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert - a callback must never be duplicated as both parameter and event
        Assert.NotNull(result);
        Assert.Single(result.Parameters);
        Assert.Equal("Label", result.Parameters[0].Name);
        Assert.Equal(2, result.Events.Count);
        Assert.Contains(result.Events, e => e.Name == "OnClosed" && e.EventArgsType is null);
        Assert.Contains(result.Events, e => e.Name == "OnClick" && e.EventArgsType == "MouseEventArgs");
    }

    [Fact]
    public void ParseSourceCode_WithFileScopedNamespace_ExtractsNamespace()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public class MudButton : MudBaseButton
            {
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudButton.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MudBlazor", result.Namespace);
    }

    [Fact]
    public void ParseSourceCode_WithCascadingParameter_IdentifiesAsCascading()
    {
        // Arrange
        var source = """
            namespace MudBlazor;

            public class MudNavLink : MudBaseComponent
            {
                [CascadingParameter]
                public MudNavMenu NavMenu { get; set; }
            }
            """;

        // Act
        var result = _parser.ParseSourceCode(source, "MudNavLink.razor.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Parameters);
        Assert.True(result.Parameters[0].IsCascading);
    }
}
