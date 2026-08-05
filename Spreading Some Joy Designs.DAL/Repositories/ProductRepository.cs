using SpreadingJoy.DAL.Context;
using SpreadingJoy.DAL.Repositories.Base;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;

namespace SpreadingJoy.DAL.Repositories;

public class ProductRepository : GenericRepository<SpreadingJoyContext, Product>, IProductRepository
{
    public ProductRepository(SpreadingJoyContext context) : base(context) { }
}
