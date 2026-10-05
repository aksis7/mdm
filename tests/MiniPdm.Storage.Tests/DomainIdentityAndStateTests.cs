using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using Xunit;

namespace MiniPdm.Storage.Tests;

/// <summary>
/// Проверяет правила идентичности объекта и допустимые переходы состояния версии.
/// </summary>
public sealed class DomainIdentityAndStateTests
{
    /// <summary>
    /// Проверяет допустимость обозначения с учётом требований к кириллическим символам.
    /// </summary>
    /// <param name="value">Обозначение, для которого проверяется допустимость кириллических символов.</param>
    /// <param name="expected">Ожидаемый результат проверки обозначения.</param>
    [Theory]
    [InlineData("АБВГ.301245.001", true)]
    [InlineData("ABВГ.301245.001", false)]
    [InlineData("АБВГ.301245.001\n", false)]
    [InlineData("АБВГ.30124.001", false)]
    public void Validates_cyrillic_designation(string value, bool expected) => Assert.Equal(expected, ObjectIdentity.IsValidDesignation(value));

    /// <summary>
    /// Проверяет обрезку, схлопывание пробелов и нечувствительность к регистру при нормализации стандартного наименования.
    /// </summary>
    [Fact]
    public void Standard_name_normalization_trims_collapses_and_ignores_case()
    {
        Assert.Equal(ObjectIdentity.NormalizeStandardName("  Болт   М8  "), ObjectIdentity.NormalizeStandardName("болт м8"));
        Assert.NotEqual(ObjectIdentity.NormalizeStandardName("Ø8×20"), ObjectIdentity.NormalizeStandardName("Ø8x20"));
    }

    /// <summary>
    /// Проверяет, разрешены ли заданные переходы между состояниями версии.
    /// </summary>
    /// <param name="from">Исходное состояние версии.</param>
    /// <param name="to">Целевое состояние версии.</param>
    /// <param name="expected">Ожидаемый результат проверки.</param>
    [Theory]
    [InlineData(VersionState.InWork, VersionState.Approved, true)]
    [InlineData(VersionState.InWork, VersionState.Cancelled, true)]
    [InlineData(VersionState.Approved, VersionState.Cancelled, true)]
    [InlineData(VersionState.Approved, VersionState.InWork, false)]
    [InlineData(VersionState.Cancelled, VersionState.InWork, false)]
    [InlineData(VersionState.Cancelled, VersionState.Approved, false)]
    public void Enforces_the_allowed_version_state_transitions(VersionState from, VersionState to, bool expected) =>
        Assert.Equal(expected, ObjectVersion.CanTransition(from, to));
}
