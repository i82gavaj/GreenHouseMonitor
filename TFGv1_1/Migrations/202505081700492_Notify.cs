namespace TFGv1_1.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class Notify : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Alerts", "IsNotification", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Alerts", "IsNotification");
        }
    }
}
