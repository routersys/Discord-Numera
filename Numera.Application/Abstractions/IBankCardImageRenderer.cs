using Numera.Application.Banking;

namespace Numera.Application.Abstractions;

public interface IBankCardImageRenderer
{
    BankCardImage? TryRender(BankCardRenderModel model);
}
