const fs = require('fs');
var f = 'Instructor/InstructorProgressReport.js';
var lines = fs.readFileSync(f, 'utf8').split('\n');
console.log('=== lines 520-545 ===');
console.log(lines.slice(519, 545).join('\n'));
