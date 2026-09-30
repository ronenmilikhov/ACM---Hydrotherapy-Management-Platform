const sql = require('mssql');
const config = {
  user: 'igroup118',
  password: 'YOUR_DB_PASSWORD',
  server: 'Media.ruppin.ac.il',
  database: 'igroup118_test2',
  options: {
    encrypt: true,
    trustServerCertificate: true
  }
};
sql.connect(config).then(pool => {
  return pool.request().query("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ReportChildren'");
}).then(result => {
  console.log(JSON.stringify(result.recordset, null, 2));
  process.exit(0);
}).catch(err => {
  console.error('Error:', err.message);
  process.exit(1);
});

