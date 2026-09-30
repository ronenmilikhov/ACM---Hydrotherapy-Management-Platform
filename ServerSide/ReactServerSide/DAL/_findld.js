const fs = require('fs');
var files = ['ServerSide/ReactServerSide/Controllers/LessonSchedulingController.cs','ServerSide/ReactServerSide/Controllers/InstructorController.cs'];
files.forEach(function(f){
  if(!fs.existsSync(f)) { console.log('MISSING ' + f); return; }
  var lines = fs.readFileSync(f, 'utf8').split('\n');
  lines.forEach(function(l, idx){
    if (/lesson-dates|LessonDates|GetLessonDatesForChild/.test(l)) {
      console.log(f.split('/').pop() + ':' + (idx+1) + ': ' + l.trim());
    }
  });
});
