using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(TFGv1_1.Startup))]
namespace TFGv1_1
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
