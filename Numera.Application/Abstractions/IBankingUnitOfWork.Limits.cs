using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public partial interface IAccountLimitPreferenceRepository
{
    void Set(DepositAccountId depositAccountId, TransferLimitSet limits);
}
