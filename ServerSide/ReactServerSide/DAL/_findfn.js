const fs = require('fs');
var f = 'Instructor/InstructorProgressReport.js';
var lines = fs.readFileSync(f, 'utf8').split('\n');
lines.forEach(function(l, idx) {
  if (/toIsoDateString|parseReportDateInput|formatReportDate/.test(l)) {
    console.log((idx+1) + ': ' + l.trim());
  }
});
