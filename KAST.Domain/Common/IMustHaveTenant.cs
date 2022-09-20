using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Domain.Common
{
    public interface IMustHaveTenant
    {
        string TenantId { get; set; }
    }
    public interface IMayHaveTenant
    {
        string? TenantId { get; set; }
    }
}