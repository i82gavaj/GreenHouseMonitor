namespace TFGv1_1.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class SupuMigration : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Sensors", "Topic", c => c.String(nullable: false, maxLength: 120));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.Sensors", "Topic", c => c.String(nullable: false, maxLength: 20));
        }
    }
}
