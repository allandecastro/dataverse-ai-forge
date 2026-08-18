using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Structural validation for the tabsJson form layout. Pure, schema-derived
/// checks (no Dataverse round-trip) shared by PlanCreateForm / PlanUpdateForm.
/// </summary>
public class FormStructureValidatorTests
{
    // ─── builders ───
    private static FormCellSpec Cell(string field) => new(Field: field);
    private static FormSectionSpec Sec(string name, params FormCellSpec[] cells) => new(name, null, cells);
    private static FormColumnSpec Col(string width, params FormSectionSpec[] sections) => new(width, sections);
    private static FormColumnSpec FullCol() => Col("100%", Sec("s", Cell("name")));
    private static FormTabSpec Tab(string name, params FormColumnSpec[] cols) => new(name, null, cols);
    private static FormStructureSpec Spec(params FormTabSpec[] tabs) => new(tabs);

    // ─── NormalizeFieldList ───

    [Fact]
    public void NormalizeFieldList_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(FormStructureValidator.NormalizeFieldList(""));
        Assert.Empty(FormStructureValidator.NormalizeFieldList("   "));
    }

    [Fact]
    public void NormalizeFieldList_SplitsTrimsAndLowercases()
    {
        // Recurring bug #3: agents pass PascalCase; Dataverse logical names are lowercase.
        var result = FormStructureValidator.NormalizeFieldList(" eppc_GiftName , Adc_Type ,,name ");
        Assert.Equal(new[] { "eppc_giftname", "adc_type", "name" }, result);
    }

    // ─── ValidateStructured: happy path ───

    [Fact]
    public void ValidateStructured_MinimalValidSpec_NoErrors()
    {
        Assert.Empty(FormStructureValidator.ValidateStructured(Spec(Tab("General", FullCol()))));
    }

    // ─── tabs / columns shape ───

    [Fact]
    public void ValidateStructured_EmptyTabs_Errors()
    {
        var errors = FormStructureValidator.ValidateStructured(Spec());
        Assert.Contains(errors, e => e.Contains("tabs array is empty"));
    }

    [Fact]
    public void ValidateStructured_TooManyColumns_Errors()
    {
        var tab = Tab("t", Col("25%", Sec("s", Cell("a"))), Col("25%", Sec("s", Cell("b"))),
            Col("25%", Sec("s", Cell("c"))), Col("25%", Sec("s", Cell("d"))));
        var errors = FormStructureValidator.ValidateStructured(Spec(tab));
        Assert.Contains(errors, e => e.Contains("exceeds the FormXML limit of 3"));
    }

    [Fact]
    public void ValidateStructured_ColumnWidthsMustSumTo100()
    {
        var tab = Tab("t", Col("50%", Sec("s", Cell("a"))), Col("40%", Sec("s", Cell("b"))));
        var errors = FormStructureValidator.ValidateStructured(Spec(tab));
        Assert.Contains(errors, e => e.Contains("sum to 90%"));
    }

    [Fact]
    public void ValidateStructured_InvalidWidthFormat_Errors()
    {
        var tab = Tab("t", Col("wide", Sec("s", Cell("a"))));
        var errors = FormStructureValidator.ValidateStructured(Spec(tab));
        Assert.Contains(errors, e => e.Contains("is invalid; must be a percentage"));
    }

    // ─── libraries + events (client scripting) ───

    [Fact]
    public void ValidateStructured_LibraryWithNonGuidUniqueId_Errors()
    {
        var spec = Spec(Tab("General", FullCol())) with
        {
            Libraries = new[] { new FormLibrarySpec(Name: "new_/lib.js", UniqueId: "not-a-guid") },
        };
        var errors = FormStructureValidator.ValidateStructured(spec);
        Assert.Contains(errors, e => e.Contains("must be a valid GUID"));
    }

    [Fact]
    public void ValidateStructured_EventReferencingUnregisteredLibrary_Errors()
    {
        var spec = Spec(Tab("General", FullCol())) with
        {
            Libraries = new[] { new FormLibrarySpec("new_/lib.js", Guid.NewGuid().ToString()) },
            Events = new[]
            {
                new FormEventSpec(EventType: "DataEvent", Name: "onload",
                    FunctionName: "Forge.onLoad", LibraryName: "other_/missing.js"),
            },
        };
        var errors = FormStructureValidator.ValidateStructured(spec);
        Assert.Contains(errors, e => e.Contains("not in the registered libraries list"));
    }

    [Fact]
    public void ValidateStructured_ControlEventWithoutAttributeOrControl_Errors()
    {
        var lib = new FormLibrarySpec("new_/lib.js", Guid.NewGuid().ToString());
        var spec = Spec(Tab("General", FullCol())) with
        {
            Libraries = new[] { lib },
            Events = new[]
            {
                new FormEventSpec("ControlEvent", "onchange", "Forge.onChange", "new_/lib.js"),
            },
        };
        var errors = FormStructureValidator.ValidateStructured(spec);
        Assert.Contains(errors, e => e.Contains("neither 'attribute' nor 'control' is set"));
    }

    // ─── display conditions ───

    [Fact]
    public void ValidateStructured_DisplayConditionsEveryoneAndRoles_Errors()
    {
        var spec = Spec(Tab("General", FullCol())) with
        {
            DisplayConditions = new FormDisplayConditionsSpec(Everyone: true, RoleIds: new[] { Guid.NewGuid().ToString() }),
        };
        var errors = FormStructureValidator.ValidateStructured(spec);
        Assert.Contains(errors, e => e.Contains("cannot set both Everyone=true and RoleIds"));
    }

    [Fact]
    public void ValidateStructured_DisplayConditionsNeitherEveryoneNorRoles_Errors()
    {
        var spec = Spec(Tab("General", FullCol())) with
        {
            DisplayConditions = new FormDisplayConditionsSpec(Everyone: false, RoleIds: null),
        };
        var errors = FormStructureValidator.ValidateStructured(spec);
        Assert.Contains(errors, e => e.Contains("requires either Everyone=true or at least one RoleId"));
    }

    // ─── ancestor form id ───

    [Fact]
    public void ValidateStructured_EmptyGuidAncestor_Errors()
    {
        var spec = Spec(Tab("General", FullCol())) with { AncestorFormId = Guid.Empty.ToString() };
        var errors = FormStructureValidator.ValidateStructured(spec);
        Assert.Contains(errors, e => e.Contains("all-zero GUID"));
    }

    // ─── ParseStructured ───

    [Fact]
    public void ParseStructured_BareTabsArray_IsWrapped_AndLegacyFlagsApply()
    {
        const string tabsJson = """[{ "name": "General", "columns": [{ "width": "100%", "sections": [{ "name": "s", "cells": [{ "field": "name" }] }] }] }]""";
        var spec = FormStructureValidator.ParseStructured(tabsJson, legacyTimeline: true, legacyHeaderCsv: "Name,Owner");

        Assert.Single(spec.Tabs);
        Assert.Equal("General", spec.Tabs[0].Name);
        Assert.True(spec.IncludeTimeline); // legacy flag merged in
        Assert.Equal(new[] { "name", "owner" }, spec.HeaderFields); // legacy header normalized
    }

    [Fact]
    public void ParseStructured_CaseInsensitiveProperties()
    {
        // Agent used PascalCase keys — parser must still bind them.
        const string tabsJson = """{ "Tabs": [{ "Name": "T", "Columns": [{ "Width": "100%", "Sections": [{ "Name": "s", "Cells": [{ "Field": "name" }] }] }] }] }""";
        var spec = FormStructureValidator.ParseStructured(tabsJson, legacyTimeline: false, legacyHeaderCsv: "");
        Assert.Single(spec.Tabs);
        Assert.Empty(FormStructureValidator.ValidateStructured(spec));
    }

    [Fact]
    public void ParseStructured_MalformedJson_Throws()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => FormStructureValidator.ParseStructured("{ not json", false, ""));
    }
}
