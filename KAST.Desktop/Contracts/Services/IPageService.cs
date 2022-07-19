using System;

namespace KAST.Desktop.Contracts.Services;

public interface IPageService
{
    Type GetPageType(string key);
}
