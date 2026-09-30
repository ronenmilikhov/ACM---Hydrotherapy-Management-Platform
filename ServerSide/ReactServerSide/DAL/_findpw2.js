const fs = require('fs');
var f = 'ServerSide/ReactServerSide/DAL/seed-more-data.js';
var c = fs.readFileSync(f, 'utf8');
var m = c.match(/Password[^\n]{0,100}/gi);
if (m) m.slice(0,10).forEach(function(x){ console.log(x.trim()); });
var m2 = c.match(/christian[^\n]{0,150}/gi);
if (m2) m2.slice(0,5).forEach(function(x){ console.log('CHR: ' + x.trim()); });
var m3 = c.match(/Instructor[^\n]{0,200}Id:\s*2[^\n]{0,200}/gi);
if (m3) m3.slice(0,3).forEach(function(x){ console.log('INST2: ' + x.trim()); });
