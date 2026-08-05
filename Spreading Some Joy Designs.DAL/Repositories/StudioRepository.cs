using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class StudioRepository : GenericRepository<SpreadingJoyContext, Studio>, IStudioRepository
{
    public StudioRepository(SpreadingJoyContext context) : base(context) { }
}
