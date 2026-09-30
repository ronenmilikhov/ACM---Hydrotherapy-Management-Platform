const fs = require('fs');
var f = 'Instructor/InstructorProgressReport.js';
var lines = fs.readFileSync(f, 'utf8').split('\n');
console.log('=== lines 255-360 ===');
console.log(lines.slice(254, 360).join('\n'));
