const sql = require('mssql');
const config = { user: 'igroup118', password: 'YOUR_DB_PASSWORD', server: 'Media.ruppin.ac.il', database: 'igroup118_test2', options: { encrypt: true, trustServerCertificate: true } };

const requiredMetrics = ['הסתגלות וביטחון במים','שליטה בנשימות (הכנסת ראש למים)','תנועתיות וקואורדינציה','יציבה וציפה','תקשורת במים (ושיתוף פעולה)','התמדה ומאמץ','יוזמה','קשב וריכוז','תגובה להוראות','עצמאות בתרגיל'];

async function updateDB() {
  let pool;
  try {
    pool = await sql.connect(config);
    const result = await pool.request().query('SELECT ReportId, Notes FROM dbo.ReportChildren');
    let updatedCount = 0;

    for (const row of result.recordset) {
      if (!row.Notes) continue;
      try {
        const notesObj = JSON.parse(row.Notes);
        if (notesObj.metrics && Array.isArray(notesObj.metrics)) {
          let modified = false;
          
          for (const metricLabel of requiredMetrics) {
            const exists = notesObj.metrics.find(m => m.label === metricLabel);
            if (!exists) {
              notesObj.metrics.push({ label: metricLabel, value: 0 });
              modified = true;
            }
          }
          
          if (modified) {
            const newNotes = JSON.stringify(notesObj);
            await pool.request()
              .input('Notes', sql.NVarChar, newNotes)
              .input('ReportId', sql.Int, row.ReportId)
              .query('UPDATE dbo.ReportChildren SET Notes = @Notes WHERE ReportId = @ReportId');
            updatedCount++;
          }
        }
      } catch (e) {
      }
    }
    console.log('Successfully updated', updatedCount, 'rows!');
  } catch (err) {
  } finally {
    if (pool) pool.close();
  }
}

updateDB();

