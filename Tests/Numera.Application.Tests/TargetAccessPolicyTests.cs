using Numera.Application.Common;

namespace Numera.Application.Tests;

[TestClass]
public sealed class TargetAccessPolicyTests
{
    private static readonly string NotFoundCode = BankingErrorCodes.DepositAccountNotFound;
    private static readonly string ForbiddenCode = BankingErrorCodes.SessionInvalid;

    [TestMethod]
    public void GrantedAccessIsRecognized()
    {
        Assert.IsTrue(TargetAccessPolicy.IsGranted(TargetAccess.Granted));
        Assert.IsFalse(TargetAccessPolicy.IsGranted(TargetAccess.Missing));
        Assert.IsFalse(TargetAccessPolicy.IsGranted(TargetAccess.NotOwned));
        Assert.IsFalse(TargetAccessPolicy.IsGranted(TargetAccess.OwnedButUnauthorized));
    }

    [TestMethod]
    public void MissingTargetIsNotFound() =>
        Assert.AreEqual(ErrorCategory.NotFound, TargetAccessPolicy.CategoryFor(TargetAccess.Missing));

    [TestMethod]
    public void ForeignTargetIsNormalizedToNotFound() =>
        Assert.AreEqual(ErrorCategory.NotFound, TargetAccessPolicy.CategoryFor(TargetAccess.NotOwned));

    [TestMethod]
    public void OwnedTargetWithoutPermissionIsForbidden() =>
        Assert.AreEqual(ErrorCategory.Forbidden, TargetAccessPolicy.CategoryFor(TargetAccess.OwnedButUnauthorized));

    [TestMethod]
    public void GrantedAccessHasNoCategory() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TargetAccessPolicy.CategoryFor(TargetAccess.Granted));

    [TestMethod]
    public void ForeignTargetUsesTheNotFoundCode()
    {
        ApplicationError error = TargetAccessPolicy.ToError(TargetAccess.NotOwned, NotFoundCode, ForbiddenCode);

        Assert.AreEqual(ErrorCategory.NotFound, error.Category);
        Assert.AreEqual(NotFoundCode, error.Code);
    }

    [TestMethod]
    public void MissingTargetUsesTheNotFoundCode()
    {
        ApplicationError error = TargetAccessPolicy.ToError(TargetAccess.Missing, NotFoundCode, ForbiddenCode);

        Assert.AreEqual(ErrorCategory.NotFound, error.Category);
        Assert.AreEqual(NotFoundCode, error.Code);
    }

    [TestMethod]
    public void UnauthorizedOwnedTargetUsesTheForbiddenCode()
    {
        ApplicationError error = TargetAccessPolicy.ToError(
            TargetAccess.OwnedButUnauthorized, NotFoundCode, ForbiddenCode);

        Assert.AreEqual(ErrorCategory.Forbidden, error.Category);
        Assert.AreEqual(ForbiddenCode, error.Code);
    }

    [TestMethod]
    public void MissingAndForeignTargetsAreIndistinguishable()
    {
        ApplicationError missing = TargetAccessPolicy.ToError(TargetAccess.Missing, NotFoundCode, ForbiddenCode);
        ApplicationError foreign = TargetAccessPolicy.ToError(TargetAccess.NotOwned, NotFoundCode, ForbiddenCode);

        Assert.AreEqual(missing.Category, foreign.Category);
        Assert.AreEqual(missing.Code, foreign.Code);
    }

    [TestMethod]
    public void BlankCodesAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => TargetAccessPolicy.ToError(TargetAccess.Missing, "  ", ForbiddenCode));
        Assert.ThrowsExactly<ArgumentException>(
            () => TargetAccessPolicy.ToError(TargetAccess.OwnedButUnauthorized, NotFoundCode, "  "));
    }
}
