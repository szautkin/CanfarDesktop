using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Services;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The ADQL pre-flight check. Ported from the Linux build's own tests, including the query CADC
/// actually refused and the two references in it that were FINE — a validator that flags working
/// queries is worse than the error it prevents, so both halves are pinned.
/// </summary>
public class AdqlValidatorTests
{
    private static TapColumn Col(string name) => new(name, "", "", "", "");

    /// <summary>The two CAOM2 tables this is all about, with the columns that matter.</summary>
    private static TapSchema Caom2() => new()
    {
        Tables =
        [
            new TapTable("caom2.Observation", "", [Col("obsID"), Col("observationID"), Col("collection")]),
            new TapTable("caom2.Plane", "", [Col("obsID"), Col("planeID"), Col("energy_bandpassName")]),
        ],
    };

    /// <summary>
    /// The query from the report, and the reason the service refused it: obsID is on BOTH tables, so a
    /// bare Plane.obsID names a column the service cannot resolve to one of them.
    /// </summary>
    [Fact]
    public void ABareTableNameOnASharedColumnIsAmbiguous()
    {
        const string adql =
            "SELECT TOP 5 Observation.observationID FROM caom2.Observation " +
            "JOIN caom2.Plane ON Plane.obsID=Observation.obsID " +
            "WHERE Observation.collection='JWST'";

        var found = AdqlValidator.Problems(adql, Caom2());

        Assert.Equal(2, found.Count); // both halves of the ON are ambiguous
        Assert.Contains("ambiguous", found[0].Message);
        Assert.Equal("caom2.Plane.obsID", found[0].Fix);
        Assert.Equal("Plane.obsID", adql[found[0].Start..found[0].End]);
    }

    /// <summary>
    /// Observation.observationID in that SAME query is fine — the service accepted it, because
    /// observationID is on one table only. "Always write an alias" is not the rule.
    /// </summary>
    [Fact]
    public void ABareTableNameOnAUniqueColumnIsLeftAlone()
    {
        const string adql =
            "SELECT Observation.observationID FROM caom2.Observation " +
            "JOIN caom2.Plane ON caom2.Plane.obsID=caom2.Observation.obsID";

        Assert.Empty(AdqlValidator.Problems(adql, Caom2()));
    }

    [Theory]
    [InlineData("SELECT o.observationID FROM caom2.Observation AS o JOIN caom2.Plane AS p ON p.obsID=o.obsID")]
    [InlineData("SELECT caom2.Observation.observationID FROM caom2.Observation JOIN caom2.Plane ON caom2.Plane.obsID=caom2.Observation.obsID")]
    [InlineData("SELECT o.obsID FROM caom2.Observation o JOIN caom2.Plane p ON p.obsID=o.obsID")] // alias without AS
    public void TheSpellingsTheServiceAcceptsAreAccepted(string adql)
        => Assert.Empty(AdqlValidator.Problems(adql, Caom2()));

    [Fact]
    public void AColumnTheTableDoesNotHaveIsNamed()
    {
        const string adql = "SELECT o.bandpass FROM caom2.Observation AS o";
        var found = AdqlValidator.Problems(adql, Caom2());

        Assert.Single(found);
        Assert.Contains("no column", found[0].Message);
        Assert.Equal("o.bandpass", adql[found[0].Start..found[0].End]);
    }

    [Fact]
    public void AnUnknownTableIsNamedWithTheClosestRealOne()
    {
        var found = AdqlValidator.Problems("SELECT p.obsID FROM caom2.Plan AS p", Caom2());

        Assert.Single(found);
        Assert.Contains("no table", found[0].Message);
        Assert.Equal("caom2.Plane", found[0].Fix);
    }

    /// <summary>
    /// Anything it cannot resolve is left alone. A subquery alias, a function call, a schema not yet
    /// fetched: each would be a false positive, and a false positive here greys out Execute on a query
    /// that works.
    /// </summary>
    [Theory]
    [InlineData("SELECT sub.obsID FROM (SELECT obsID FROM caom2.Plane) AS whatever")]
    [InlineData("SELECT 1")]
    public void WhatItCannotResolveItDoesNotReport(string adql)
        => Assert.Empty(AdqlValidator.Problems(adql, Caom2()));

    [Fact]
    public void WithNoSchemaNothingIsKnowable()
    {
        // The schema arrives asynchronously; for the first second of every session this is the state.
        Assert.Empty(AdqlValidator.Problems("SELECT x.y FROM caom2.Observation AS x", null));
        Assert.Empty(AdqlValidator.Problems("SELECT x.y FROM caom2.Observation AS x", new TapSchema()));
    }

    /// <summary>Case is not a mistake: ADQL identifiers are case-insensitive unquoted.</summary>
    [Fact]
    public void CaseDoesNotMakeAQueryWrong()
        => Assert.Empty(AdqlValidator.Problems("select O.OBSID from CAOM2.OBSERVATION as O", Caom2()));

    [Fact]
    public void AProblemDescribesItselfTheSameWayEverywhere()
    {
        var withFix = new AdqlProblem(0, 5, "x.y is ambiguous", "caom2.Plane.y");
        Assert.Equal("x.y is ambiguous — write caom2.Plane.y", withFix.Describe());

        var without = new AdqlProblem(0, 5, "no table \"z\" in this service", null);
        Assert.Equal("no table \"z\" in this service", without.Describe());
    }
}
