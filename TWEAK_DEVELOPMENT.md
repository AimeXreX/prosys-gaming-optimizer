# Tweak development

Every tweak implements `ITweak` and supplies a stable ID, revision, reason, risk vector, benefit/evidence classification, build scope, dependency metadata and recovery description. Its lifecycle is detect → compatibility → backup → apply → verify → rollback → verify rollback.

A tweak is not eligible for the Safe profile unless it is evidence-backed, easy to reverse, has no security impact, and has at most low overall risk and very-low stability risk. Tests must cover original-value capture, read-back verification, absent-value rollback and repeated rollback. Hardware-sensitive, undocumented or security-reducing changes must not be added to Safe.
