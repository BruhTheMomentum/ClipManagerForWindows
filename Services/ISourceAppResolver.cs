using System;
using System.Threading.Tasks;

namespace ClipManagerForWindows.Services;

public interface ISourceAppResolver
{
    string? TryGetForegroundProcessName();
}
