const fs = require('fs');
var f = 'Instructor/InstructorProgressReport.js';
var lines = fs.readFileSync(f, 'utf8').split('\n');
console.log('=== lines 1010-1075 ===');
console.log(lines.slice(1009, 1075).join('\n'));
