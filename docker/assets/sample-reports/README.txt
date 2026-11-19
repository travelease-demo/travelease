TravelEase - sample report files
================================

These files were packed into docker/assets/sample-reports.tar.gz and extracted into
/app/data/reports by the ADD instruction in docker/Dockerfile.instructions.

ADD automatically extracts LOCAL tar archives (.tar, .tar.gz, .tgz, .tar.bz2, .tar.xz).
COPY would have copied the archive file unchanged.

Because /app/data is declared as a VOLUME, Docker copies these files into a new, empty named
volume the first time it is mounted. Files you export from the Reports page are written next
to them and survive container removal as long as the volume exists.
