const fs = require('fs');
var f = 'ServerSide/ReactServerSide/Controllers/LessonSchedulingController.cs';
var lines = fs.readFileSync(f, 'utf8').split('\n');
console.log('=== lines 40-90 ===');
console.log(lines.slice(39, 90).join('\n'));
