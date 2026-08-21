/* Copyright © 2023-2024 Björn Victor (bjorn@victor.se) */
/* Code to make FILE.c with defined SELECT run with cbridge instead of "local", in-memory Chaos. */
/*
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <string.h>

// opcodes etc, needed for chncp.h
#include "chaos.h"
#define ACKOP 0177		/* cbridge specific */
// stuff we're not using, but needed for chncp.h
#include "chunix/chconf.h"
// struct connection, packet etc
#include "chncp/chncp.h"

#include "cbncp.h"

#ifdef DMALLOC
#include "dmalloc.h"
#endif

// from FILE.c
extern void processdata(struct connection *conn);
#ifdef MAP_ROOT_DIRECTORY
extern void settreeroot(const char *root, const char *prefix);
#endif

// Dang
void *
ch_alloc(int size, int cantwait)
{
	return calloc(size, sizeof(void *));
}
void
ch_free(void *p)
{
	free((void *)p);
}


void usage(char *pname)
{
  fprintf(stderr,"Usage: %s -r rootdir\n"
	  " where rootdir is the root directory of the FILE server\n", pname);
  exit(1);
}

int main(int argc, char *argv[])
{
  signed char c;		/* gaah. */
  extern char *optarg;
  char *fsroot, *rootarg = NULL;

  while ((c = getopt(argc, argv, "hr:")) != -1) {
    switch (c) {
    case 'r':
      rootarg = strdup(optarg);
      break;
    case 'h':
    default:
      usage(argv[0]);
    }
  }
  if (rootarg == NULL) {
    usage(argv[0]);
    exit(1);
  }
  fsroot = realpath(rootarg,NULL);
  if (fsroot == NULL) {
    fprintf(stderr,"Can not find the real pathname of \"%s\".\n", rootarg);
    exit(1);
  } else
    fprintf(stderr,"Using root directory \"%s\"\n", fsroot);
#ifdef MAP_ROOT_DIRECTORY
  settreeroot(fsroot,NULL);
#else
#error You really must compile this with MAP_ROOT_DIRECTORY defined to be safe.
#endif
  cb_listen_main("FILE", &processdata);
  return 0;
}
