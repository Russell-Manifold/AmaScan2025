using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AmaScan.sqliteModels
{
    public interface IIdentifiable
      {
        int Id { get; set; }  // Every class that implements this interface must have an 'Id' property.
    }
}
