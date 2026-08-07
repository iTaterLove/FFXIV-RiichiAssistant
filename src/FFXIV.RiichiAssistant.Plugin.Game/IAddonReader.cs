using FFXIV.RiichiAssistant.Core;

namespace FFXIV.RiichiAssistant.Plugin.Game;

public interface IAddonReader
{
    Result<MahjongTableSnapshot, ReadError> ReadSnapshot();
}
